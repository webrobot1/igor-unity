# Ведомая копия. Источник — /var/www/html/game/.claude/hooks/lib/write_targets.py в серверном
# репозитории; копию перезаписывает его хук при каждой правке источника. Правка копии теряется
# молча — вносить её в источник. Серверного репозитория нет под рукой → назвать нужную правку
# пользователю.

"""Цели ЗАПИСИ в команде оболочки — общий носитель хуков, которым нужно знать, какие файлы правит
вызов: гейт владения путями (skill-guard.sh) и напоминание о своде домена (mcp-context-reminder.sh).
Копия разбора у второго разошлась бы с первым молча — перехватываемые формы живут здесь.
Владения, зон и политики отказа модуль не знает: он отдаёт абсолютные пути, решение принимает
вызывающий.

Что ловит разбор: редирект (`>`, `>>`, `2>`, `&>`, слитный `>файл`, вход heredoc), `sed -i`,
`perl -i`, `tee`, `cp`, `mv`, `install`, `rsync`, `ln`, `rm`, `touch`, `truncate`, `dd of=`,
`mkdir`, `rmdir`, `chmod`, `chown`, `git mv`, `git rm`, те же вызовы под `sudo`/`env` и внутри
`sh -c '…'`; цель, заданную подстановкой (`"$f"`), — запасным проходом по литералам.
Код на интерпретаторе разбирается независимо от ФОРМЫ подачи: инлайн-флаг (`-c`, `-e`) и тело
heredoc (`python3 - <<'PY'`) читаются одинаково; тело heredoc, поданное оболочке (`bash <<'SH'`),
разбирается цепочкой команд. Тело, поданное чужой команде (`cat > файл <<'EOF'`, ввод `sed`),
остаётся содержимым и целей не даёт.
Что проходит МИМО (принято осознанно, чтобы не бить по чтению): запись из кода на интерпретаторе
без литерала пути в самом коде; путь, собранный из переменных целиком; путь, стоящий в коде НЕ
первым в строковом литерале (текст, строка-команда чужому инструменту); запись через
запущенный из оболочки СКРИПТ либо чужой инструмент, чей аргумент путём не выглядит; `install`
с целью-каталогом, вычисляемым на месте.
Чтение целей не даёт вовсе: `grep`, `cat`, `sed -n`, `head`, `awk` без редиректа. Запуск наравне
с чтением: имя запускаемой команды и путь скрипта, поданный оболочке либо интерпретатору, целью
не бывают ни в основном разборе, ни в запасном проходе — исполнение файла его правкой не является.

Разрез цепочки и позиция имени команды — chain_parser.py, носитель уровнем ниже.
Расхождение с обоими источниками ловит .claude/hooks/command-guard.test.sh."""

import os
import re

from chain_parser import SHELLS, command_index, split_parts, tokens, name

HEREDOC = re.compile(r'<<-?\s*(["\']?)([A-Za-z_][A-Za-z0-9_]*)\1')
REDIR_FULL = re.compile(r'^(?:\d+|&)?(?:>>?|>\|)$')
REDIR_HEAD = re.compile(r'^(?:\d+|&)?(?:>>?|>\|)(?=[^>|])')
INPLACE = re.compile(r'^-[A-Za-z]*i')

GIT_VALUE_OPTS = {'-C', '-c', '--git-dir', '--work-tree', '--namespace', '--exec-path',
                  '--config-env'}
INTERPRETERS = {'python', 'python3', 'perl', 'php', 'node', 'ruby'}
INLINE_FLAGS = {'-c', '-e', '-r', '--eval'}
# Признаки ЗАПИСИ в коде интерпретатора: без них строка с путём — чтение, не цель. Каждая форма
# стоит своей строкой: `.write_text(` подстрокой `.write(` не является, семейством они не берутся.
WRITE_MARKS = ('.write(', '.write_text(', '.write_bytes(', 'file_put_contents', 'writeFile',
               'unlink', 'os.remove', 'shutil.', 'fwrite', 'fputs', 'rename(')
# Режим открытия — признак записи только АРГУМЕНТОМ вызова: за ним стоит запятая либо закрывающая
# скобка. Голая строка режима признаком не служит — тем же текстом код несёт ключ словаря и букву,
# и по ней пишущим читается снимок, который только читает.
WRITE_MODES = ('w', 'a', 'x', 'w+', 'a+', 'r+', 'wb', 'ab', 'xb', 'wb+', 'ab+', 'rb+', 'w+b', 'a+b')
WRITE_MODE = re.compile(r",\s*(?:mode\s*=\s*)?(['\"])(?:%s)\1\s*[,)]"
                        % "|".join(re.escape(m) for m in WRITE_MODES))
# Вызовы, чей ПЕРВЫЙ аргумент — адрес: имя, стоящее там, ведёт к литералу через своё присваивание.
# Перечень идёт парой к WRITE_MARKS — незнакомая форма даёт тихий пропуск адреса-переменной, потому
# формы именуются поимённо, семейством не берутся.
ADDR_CALLS = ('open', 'fopen', 'Path', 'file_put_contents', 'unlink', 'remove', 'rename',
              'writeFile', 'writeFileSync', 'copy', 'copyfile', 'move')
ADDR_VAR = re.compile(r"\b(?:%s)\s*\(\s*([A-Za-z_][A-Za-z0-9_]*)\s*[,)]"
                      % "|".join(ADDR_CALLS))
# Присваивание именем: значение справа несёт литерал-адрес.
CODE_ASSIGN = re.compile(r"\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.+)$", re.S)
# Адрес записи в коде — НАЧАЛО строкового литерала, а за адресом внутри литерала стоит либо его
# конец, либо подстановка (`'/дир/x.md'`, `'/дир/%s.md' % n`, `'/дир/' + имя`).
# Путь, стоящий внутри текста, и текст за путём — СОДЕРЖИМОЕ, которое код пишет, не адрес, куда он
# пишет: тем же литералом код несёт цитату правила, строку отчёта, шаблон поиска. Тот же якорь
# отсекает хвост подстановки: у `'$D/x.md'` литерал начинается именем переменной, и целиком путь из
# текста не виден.
# Имя файла без каталога — только литералом ЦЕЛИКОМ: голое слово с точкой несёт в коде что угодно
# (версия, число). Без второй формы код, вставший в каталог цели (`cd зона`, `os.chdir`), правит её
# файл по голому имени мимо разбора.
CODE_PATH = re.compile(r"(?<=['\"])(?:[\w.~-]*/[\w./*-]+(?=['\"%{$])"
                       r"|[\w.@+-]+\.[A-Za-z0-9]{1,8}(?=['\"]))")

ALL_TARGETS = {'rm', 'touch', 'tee', 'truncate', 'mkdir', 'rmdir', 'unlink', 'shred'}
# Отсутствие цели в аргументах значит «цель приходит потоком». У `tee` оно значит другое —
# вывод только в stdout, файла нет вовсе.
STDIN_TARGETS = ALL_TARGETS - {'tee'}
DEST_LAST = {'cp', 'mv', 'install', 'rsync', 'ln'}
SKIP_FIRST = {'chmod', 'chown', 'chgrp'}
VALUE_OPTS = {
    'truncate': {'-s', '--size', '-r', '--reference'},
    'touch': {'-d', '--date', '-r', '--reference', '-t'},
    'mkdir': {'-m', '--mode'},
    'cp': {'-S', '--suffix', '--sparse', '--preserve'},
    'mv': {'-S', '--suffix'},
    'install': {'-m', '--mode', '-o', '--owner', '-g', '--group', '-S', '--suffix'},
    'ln': {'-S', '--suffix'},
    'rsync': {'-e', '--rsh', '--exclude', '--include'},
}
TARGET_DIR_OPTS = {'-t', '--target-directory'}

# Имя переменной в неразрешимой цели: по нему ищется её источник в той же команде.
VAR_REF = re.compile(r'\$\{?([A-Za-z_][A-Za-z0-9_]*)')
# Слева от имени — граница части либо пробел: иначе `--target-directory=` читалось бы
# присваиванием переменной `directory`.
VAR_EDGE = r'(?:^|[;&|()\n]|\s)'
ASSIGN_HEAD = VAR_EDGE + r'(?:export\s+|local\s+|declare\s+|typeset\s+|readonly\s+)?%s='
FOR_HEAD = VAR_EDGE + r'for\s+%s\s+in\s'
SOURCE_DEPTH = 3


def bare(token):
    # Запасной проход и проход по инлайн-коду токенизируют СЫРУЮ команду в обход split_parts —
    # оттого к токену прилипает хвост разреза цепочки (`/путь/x.md)` из `$(… /путь/x.md)`).
    return token.strip('"\'').strip('();,')


def normalize(target, cwd):
    if not target.startswith('/'):
        target = os.path.join(cwd, target)
    return os.path.normpath(target)


def _stdin_script(args):
    """Тело heredoc идёт вызову ПРОГРАММОЙ: своего файла-скрипта и инлайн-кода у него нет, читать
    он будет stdin. Иначе тело — ввод уже названной программы, то есть данные."""
    for a in args:
        if a in INLINE_FLAGS:
            return False
        if a == '-' or a.startswith('<<'):
            continue
        if not a.startswith('-'):
            return False
    return True


def _heredoc_kind(header):
    """Чем тело heredoc является по его заголовочной строке: `shell` — цепочкой команд
    (`bash <<SH`), `code` — кодом интерпретатора (`python3 - <<PY`), None — данными
    (`cat > файл <<EOF`, ввод чужой команды)."""
    for part in split_parts(header):
        if not HEREDOC.search(part):
            continue
        toks = strip_redirects(tokens(part))
        i = command_index(toks)
        if i >= len(toks):
            continue
        cmd = name(bare(toks[i]))
        if not _stdin_script(toks[i + 1:]):
            continue
        if cmd in SHELLS:
            return 'shell'
        if cmd in INTERPRETERS:
            return 'code'
    return None


def _split_heredocs(command):
    """Команда без тел heredoc и сами тела, поданные на ИСПОЛНЕНИЕ: (текст, [(вид, тело)]).
    Тело-данные — содержимое файла, не команда: строка `> цитата` в нём целью редиректа не
    является, и в перечень тел оно не идёт. Заголовочная строка с самим редиректом остаётся."""
    lines = command.split('\n')
    kept, bodies, i = [], [], 0
    while i < len(lines):
        kept.append(lines[i])
        m = HEREDOC.search(lines[i])
        kind = _heredoc_kind(lines[i]) if m else None
        i += 1
        if not m:
            continue
        delim, body = m.group(2), []
        while i < len(lines) and lines[i].strip() != delim:
            body.append(lines[i])
            i += 1
        i += 1
        if kind:
            bodies.append((kind, '\n'.join(body)))
    return '\n'.join(kept), bodies


def strip_heredocs(command):
    return _split_heredocs(command)[0]


def heredoc_bodies(command):
    return _split_heredocs(command)[1]


def redirect_targets(toks):
    res, i = [], 0
    while i < len(toks):
        t = toks[i]
        if REDIR_FULL.match(t):
            if i + 1 < len(toks):
                res.append(toks[i + 1])
            i += 2
            continue
        m = REDIR_HEAD.match(t)
        if m:
            res.append(t[m.end():])
        i += 1
    return res


def redirect_free(toks):
    """Токены части без перенаправлений, каждый со СВОИМ индексом в исходном списке: запасной
    проход адресует пропускаемое позицией и считает её по этому же списку."""
    res, i = [], 0
    while i < len(toks):
        t = toks[i]
        if REDIR_FULL.match(t):
            i += 2
            continue
        if REDIR_HEAD.match(t):
            i += 1
            continue
        res.append((i, t))
        i += 1
    return res


def strip_redirects(toks):
    return [t for _, t in redirect_free(toks)]


def run_positions(toks):
    """Индексы токенов, которые ИСПОЛНЯЮТСЯ, а не правятся: имя самой команды и путь скрипта,
    поданный оболочке либо интерпретатору на запуск. Основной разбор их целью не берёт — там цель
    даёт только операция записи; запасной проход целей не разбирает вовсе и без этого списка выдал
    бы запускаемый скрипт за правимый. Позиция считается по КАЖДОЙ части отдельно: тот же путь,
    стоящий в другой части аргументом (перечень цикла, маска), остаётся под проверкой."""
    pairs = redirect_free(toks)
    plain = [t for _, t in pairs]
    i = command_index(plain)
    if i >= len(plain):
        return set()
    res = {pairs[i][0]}
    if name(bare(plain[i])) in SHELLS | INTERPRETERS:
        k = i + 1
        while k < len(plain):
            a = plain[k]
            if a in INLINE_FLAGS:
                break            # дальше идёт КОД, а не путь: его цели ищет interpreter_pass
            if a.startswith('-'):
                k += 1
                continue
            res.add(pairs[k][0])
            break
    return res


def args_targets(args, value_opts, mode):
    plain, forced, i = [], [], 0
    while i < len(args):
        a = args[i]
        if a.startswith('-') and a != '-':
            if a in TARGET_DIR_OPTS and i + 1 < len(args):
                forced.append(args[i + 1])
                i += 2
                continue
            if a.startswith('--target-directory='):
                forced.append(a.split('=', 1)[1])
                i += 1
                continue
            if a in value_opts and i + 1 < len(args):
                i += 2
                continue
            i += 1
            continue
        plain.append(a)
        i += 1
    if forced:
        return forced
    if not plain:
        return []
    if mode == 'last':
        return plain[-1:]
    if mode == 'skip_first':
        return plain[1:]
    return plain


def sed_inplace(args):
    return any(a == '--in-place' or a.startswith('--in-place=')
               or (a.startswith('-') and not a.startswith('--') and INPLACE.match(a))
               for a in args)


def perl_inplace(args):
    return any(a == '--in-place'
               or (a.startswith('-') and not a.startswith('--') and INPLACE.match(a))
               for a in args)


def sed_targets(args):
    if not sed_inplace(args):
        return []
    plain, has_expr, i = [], False, 0
    while i < len(args):
        a = args[i]
        if a in ('-e', '-f', '--expression', '--file'):
            has_expr = True
            i += 2
            continue
        if a.startswith('--expression=') or a.startswith('--file='):
            has_expr = True
            i += 1
            continue
        if a.startswith('-') and a != '-':
            i += 1
            continue
        plain.append(a)
        i += 1
    # Без -e/-f первый не-флаговый аргумент — сам скрипт замены, не файл.
    return plain if has_expr else plain[1:]


def perl_targets(args):
    if not perl_inplace(args):
        return []
    plain, i = [], 0
    while i < len(args):
        a = args[i]
        if a in ('-e', '-E', '-M', '-m', '-I'):
            i += 2
            continue
        if a.startswith('-') and a != '-':
            i += 1
            continue
        plain.append(a)
        i += 1
    return plain


def not_a_target(t):
    """Токен целью не бывает: пустой, одиночный `-`, дескриптор, перенаправление, `/dev/*`."""
    return (not t or t == '-' or t.startswith('&') or t.startswith('/dev/')
            or '>' in t or '<' in t)


def _record(raw, fragment, found, unresolved, cwd):
    t = bare(raw)
    if not_a_target(t):
        return
    if '$' in t or '`' in t or t.startswith('~'):
        # Сам токен идёт в перечень: по имени переменной в нём запасной проход находит источник
        # значения и сужает область поиска литералов до него.
        unresolved.append((t, fragment))
        return
    found.append((normalize(t, cwd), fragment))


def code_statements(code):
    """Выражения кода: границы — перевод строки и `;` ВНЕ строкового литерала и вне незакрытых
    скобок. Скобки держат вызов, записанный в несколько строк, одним выражением: иначе адрес и
    признак записи расходятся по разным выражениям и цель теряется."""
    out, buf, quote, depth, i, n = [], [], None, 0, 0, len(code)
    while i < n:
        ch = code[i]
        if quote:
            buf.append(ch)
            if ch == '\\' and i + 1 < n:
                buf.append(code[i + 1])
                i += 2
                continue
            if ch == quote:
                quote = None
            i += 1
            continue
        if ch in '"\'':
            quote = ch
            buf.append(ch)
            i += 1
            continue
        if ch in '([{':
            depth += 1
        elif ch in ')]}':
            depth = max(0, depth - 1)
        if (ch == ';' or ch == '\n') and depth == 0:
            out.append(''.join(buf))
            buf = []
            i += 1
            continue
        buf.append(ch)
        i += 1
    out.append(''.join(buf))
    return [s for s in out if s.strip()]


def _code_targets(code, cwd, found, unresolved):
    """Цели записи в КОДЕ интерпретатора — общий носитель обеих форм подачи, инлайн-флага и тела
    heredoc: форма подачи перехват не определяет. Что считается адресом внутри кода, объявлено у
    CODE_PATH.
    Литерал берётся целью, лишь когда стоит в ВЫРАЖЕНИИ с признаком записи: тем же кодом адресуют
    чтение (импорт модуля, разбор файла), поиск и перечень проб, и признак записи, взятый по ВСЕМУ
    коду, выдаёт такой адрес за цель — отказ приходит на read-only работу и называет чужого
    владельца. Адрес, поданный через переменную, разворачивается по её присваиванию, и только
    когда имя стоит АДРЕСОМ вызова (ADDR_CALLS): переменная в позиции СОДЕРЖИМОГО несёт то, что
    пишут, а не куда."""
    stmts = code_statements(code)
    writing = [s for s in stmts
               if any(m in s for m in WRITE_MARKS) or WRITE_MODE.search(s)]
    if not writing:
        return

    names = set()
    for stmt in writing:
        for m in re.finditer(CODE_PATH, stmt):
            _record(m.group(), stmt[:200], found, unresolved, cwd)
        names.update(ADDR_VAR.findall(stmt))

    if not names:
        return

    for stmt in stmts:
        m = CODE_ASSIGN.match(stmt)
        if not m or m.group(1) not in names:
            continue
        for lit in re.finditer(CODE_PATH, m.group(2)):
            _record(lit.group(), stmt[:200], found, unresolved, cwd)


def _inline_code(command):
    """Сам код инлайн-вызова: токен за флагом кода. Внешние кавычки аргумента снимает
    токенизация — без неё `;` кода стоят внутри строки и выражений в нём не видно.
    Токенизация не разобрала команду (кавычки собраны подстановкой) — кодом берётся вся команда,
    как прежде: охват важнее точности границ."""
    toks = tokens(command.replace('\n', ' '))
    for k, t in enumerate(toks):
        if name(bare(t)) not in INTERPRETERS:
            continue
        for j in range(k + 1, min(k + 6, len(toks))):
            if toks[j] in INLINE_FLAGS:
                return toks[j + 1] if j + 1 < len(toks) else command
    return None


def _interpreter_pass(command, cwd, found, unresolved):
    """Код интерпретатора идёт ОДНОЙ строкой-аргументом, а разрез цепочки режет её по скобкам —
    разобрать её как команду нельзя. Разбирается он кодом, и только когда рядом с именем
    интерпретатора стоит флаг инлайн-кода."""
    code = _inline_code(command)
    if code is not None:
        _code_targets(code, cwd, found, unresolved)


def _run_bodies(bodies, cwd, found, unresolved, depth=1):
    """Тела heredoc, поданные на исполнение: shell-тело разбирается цепочкой команд, код
    интерпретатора — литералами кода. Вложенный heredoc внутри shell-тела разбирается тем же
    порядком; у кода интерпретатора вложенных тел не бывает — `<<` там оператор языка."""
    if depth > 3:
        return
    for kind, body in bodies:
        if kind != 'shell':
            _code_targets(body, cwd, found, unresolved)
            continue
        text, inner = _split_heredocs(body)
        _scan(text, cwd, found, unresolved, depth)
        _interpreter_pass(text, cwd, found, unresolved)
        _run_bodies(inner, cwd, found, unresolved, depth + 1)


def _scan(command, cwd, found, unresolved, depth=0):
    if depth > 3:
        return cwd
    for part in split_parts(command):
        part = part.strip()
        if not part:
            continue
        toks = tokens(part)
        if not toks:
            continue
        for tgt in redirect_targets(toks):
            _record(tgt, part, found, unresolved, cwd)
        toks = strip_redirects(toks)
        i = command_index(toks)
        if i >= len(toks):
            continue
        cmd = name(bare(toks[i]))
        args = toks[i + 1:]
        if cmd in SHELLS:
            for k, a in enumerate(args):
                if a == '-c' and k + 1 < len(args):
                    _scan(args[k + 1], cwd, found, unresolved, depth + 1)
                    break
                if not a.startswith('-'):
                    break
            continue
        if cmd == 'cd' and args:
            # Каталог оболочки сдвинут внутри самой команды — дальше относительные цели считаются
            # от него, не от cwd вызова.
            t = bare(args[0])
            if '$' not in t and '`' not in t and not t.startswith('~'):
                cwd = normalize(t, cwd)
            continue
        if cmd == 'find':
            # Цель у `find` — заполняемый обходом `{}`: из аргументов её не видно. Помечаем
            # неразрешимой ТОЛЬКО когда исполняется операция записи, иначе `-exec grep` по
            # защищённому каталогу отбивался бы как правка. Имени переменной у такой цели нет —
            # область запасного прохода не сужается.
            for k, a in enumerate(args):
                if a == '-delete':
                    unresolved.append((None, part))
                elif a in ('-exec', '-execdir') and k + 1 < len(args):
                    nested, nargs = name(bare(args[k + 1])), args[k + 2:]
                    if (nested in ALL_TARGETS or nested in DEST_LAST or nested in SKIP_FIRST
                            or (nested == 'sed' and sed_inplace(nargs))
                            or (nested == 'perl' and perl_inplace(nargs))):
                        unresolved.append((None, part))
            continue
        tgts = []
        if cmd == 'sed':
            tgts = sed_targets(args)
        elif cmd == 'perl':
            tgts = perl_targets(args)
        elif cmd == 'dd':
            tgts = [a.split('=', 1)[1] for a in args if a.startswith('of=')]
        elif cmd == 'git':
            k = 0
            while k < len(args) and args[k].startswith('-'):
                k += 2 if args[k] in GIT_VALUE_OPTS else 1
            if k < len(args):
                sub, rest = bare(args[k]), args[k + 1:]
                if sub == 'rm':
                    tgts = args_targets(rest, set(), 'all')
                elif sub == 'mv':
                    tgts = args_targets(rest, set(), 'last')
        elif cmd in ALL_TARGETS:
            tgts = args_targets(args, VALUE_OPTS.get(cmd, set()), 'all')
        elif cmd in DEST_LAST:
            tgts = args_targets(args, VALUE_OPTS.get(cmd, set()), 'last')
        elif cmd in SKIP_FIRST:
            tgts = args_targets(args, set(), 'skip_first')
        if not tgts and (cmd in STDIN_TARGETS or (cmd == 'sed' and sed_inplace(args))
                         or (cmd == 'perl' and perl_inplace(args))):
            # Операция записи есть, а цели в аргументах нет: она приходит потоком
            # (`… | xargs sed -i 's/a/b/'`). Что правится, из самой команды не видно, имени
            # переменной тоже нет — область запасного прохода не сужается.
            unresolved.append((None, part))
        for t in tgts:
            _record(t, part, found, unresolved, cwd)

    # Каталог на конец цепочки: `cd` внутри неё сдвигает его и для запасного прохода.
    return cwd


def parent_on_disk(path):
    """Родительский каталог цели существует. У склейки относительного литерала с чужим каталогом
    родителя нет вовсе — она и есть выдуманный путь; у настоящей цели он есть, даже когда сам файл
    ещё только создаётся. Маска цикла родителя не называет: якорем берётся ближайший каталог выше
    без метасимволов."""
    anchor = os.path.dirname(path)
    while anchor and any(ch in anchor for ch in '*?['):
        up = os.path.dirname(anchor)
        if up == anchor:
            break
        anchor = up

    return os.path.isdir(anchor)


def var_names(raw):
    """Имена переменных в токене. Пусто — значение приходит подстановкой команды (`$(…)`,
    обратные кавычки), позиционным параметром либо тильдой: назвать источник нечем."""
    return VAR_REF.findall(raw or '')


def _for_list(command, start):
    """Перечень цикла: от `in` до конца заголовка. Границы — `;`, перевод строки и слово `do`."""
    tail = command[start:]
    for ch in (';', '\n'):
        p = tail.find(ch)
        if p != -1:
            tail = tail[:p]
    m = re.search(r'(?:^|\s)do(?:\s|$)', tail)
    return tail[:m.start()] if m else tail


def _read_word(command, start):
    """Правая часть присваивания: до пробела либо границы части, но подстановка команды и текст в
    кавычках читаются целиком — `f=$(ls .claude/skills/*.md | head -1)` иначе оборвался бы на
    первом же пробеле, и литерал зоны из источника выпал бы."""
    out, i, n, depth, quote = [], start, len(command), 0, None
    while i < n:
        ch = command[i]
        if quote:
            out.append(ch)
            if ch == '\\' and quote == '"' and i + 1 < n:
                out.append(command[i + 1])
                i += 2
                continue
            if ch == quote:
                quote = None
            i += 1
            continue
        if ch == '\\' and i + 1 < n:
            out.append(ch)
            out.append(command[i + 1])
            i += 2
            continue
        if ch in '"\'':
            quote = ch
            out.append(ch)
            i += 1
            continue
        if ch == '(':
            depth += 1
        elif ch == ')':
            if depth == 0:
                break
            depth -= 1
        elif depth == 0 and (ch.isspace() or ch in ';&|'):
            break
        out.append(ch)
        i += 1
    return ''.join(out)


def _sources_of(command, var):
    res = [_for_list(command, m.end())
           for m in re.finditer(FOR_HEAD % re.escape(var), command)]
    res += [_read_word(command, m.end())
            for m in re.finditer(ASSIGN_HEAD % re.escape(var), command)]
    return [t for t in res if t.strip()]


def var_sources(command, names, depth=0, seen=None):
    """Тексты, откуда переменные берут значение: перечень `for X in …` и правая часть `X=…` в той
    же команде. None — источника хотя бы одного имени в команде нет: значение пришло извне, сузить
    область нечем."""
    if depth > SOURCE_DEPTH:
        return None
    seen = set() if seen is None else seen
    texts = []
    for var in names:
        if var in seen:
            continue
        seen.add(var)
        got = _sources_of(command, var)
        if not got:
            return None
        for text in got:
            texts.append(text)
            deeper = var_sources(command, var_names(text), depth + 1, seen)
            if deeper is None:
                return None
            texts.extend(deeper)
    return texts


def sweep_scope(command, unresolved):
    """Область запасного прохода. Список текстов — источники значений неразрешимых целей: literal
    ищется ТОЛЬКО в них. None — вся команда: у цели нет имени переменной либо её источник вне
    команды.
    Без сужения любой литерал-путь в команде становится целью неразрешимого редиректа, с которым
    не связан ничем: чтение с выводом во временный файл (`for f in <зона>/*; do … >> "$tmp"; done`)
    отбивалось бы как правка зоны."""
    texts = []
    for raw, _fragment in unresolved:
        if raw is None:
            return None
        names = var_names(raw)
        if not names:
            return None
        got = var_sources(command, names)
        if got is None:
            return None
        texts.extend(got)
    return texts


def literal_sweep(command, cwd, runnable=True):
    """Цель задана подстановкой — что именно правится, из аргумента не видно. Тогда путь ищется
    литералом по тексту: массовая правка обычно несёт его в перечне либо маске цикла.

    Каталог тут ЭФФЕКТИВНЫЙ — со сдвигом `cd` внутри цепочки, тем же, что считает _scan().
    Разрешённая от каталога ВЫЗОВА относительная цель после `cd` склеивается в несуществующий путь
    и попадает в чужую зону: отказ приходит на read-only команду и называет НЕВЕРНОГО владельца.
    Токен с пробелом внутри путём не бывает — он приходит из текста в кавычках.
    Относительный литерал берётся целью, лишь когда его родительский каталог есть на диске: каталог
    вызова сам бывает внутри защищённой зоны, и любой относительный литерал склеивается ВНУТРЬ неё,
    получая её владельца, — файла по такому пути не существует вовсе. Абсолютный литерал пробы не
    требует: он не склеивается.
    Исполняемое из проверки выпадает по ПОЗИЦИИ в своей части (run_positions), не по значению:
    путь, запускаемый в одной части и стоящий аргументом в другой, проверку проходит целиком.
    runnable=False — текст не команда, а источник значения переменной (перечень цикла, правая часть
    присваивания): исполняемого в нём нет, и первый токен там такая же цель, как прочие."""
    res = []
    for part in split_parts(command):
        toks = tokens(part)
        skip = run_positions(toks) if runnable else set()
        for pos, tok in enumerate(toks):
            if pos in skip:
                continue
            t = bare(tok)
            if '$' in t or '`' in t or t.startswith('~'):
                continue
            # Пробел внутри токена — текст в кавычках, не путь: shlex отдаёт такую строку одним
            # токеном.
            if not_a_target(t) or any(ch.isspace() for ch in t):
                continue
            # Каталог цикла часто заводится присваиванием (`D=/путь/skills`) — значение справа тот
            # же литерал, а целиком токен путём не является.
            if '=' in t and not t.startswith('='):
                t = t.split('=', 1)[1].strip('"\'')
            if '/' not in t and not t.endswith('.md'):
                continue
            target = normalize(t, cwd)
            if not t.startswith('/') and not parent_on_disk(target):
                continue
            res.append(target)
    return res


def scan_command(command, cwd):
    """Разбор команды: (цели записи, неразрешимые цели, каталог на конец цепочки, команда без тел
    heredoc). Цели — абсолютные пути с фрагментом команды, породившим каждую."""
    stripped, bodies = _split_heredocs(command or "")
    found, unresolved = [], []
    eff_cwd = _scan(stripped, cwd, found, unresolved)
    # Каталог у кода ЭФФЕКТИВНЫЙ — тот же, что у запасного прохода: `cd` внутри цепочки сдвигает
    # его и интерпретатору, а относительная цель, разрешённая от каталога ВЫЗОВА, склеивается в
    # несуществующий путь и уводит отказ к чужому владельцу.
    _interpreter_pass(stripped, eff_cwd, found, unresolved)
    _run_bodies(bodies, eff_cwd, found, unresolved)
    return found, unresolved, eff_cwd, stripped


def sweep(command, cwd, unresolved):
    """Запасной проход по литералам: пути из области, связанной с неразрешимыми целями."""
    scope = sweep_scope(command, unresolved)
    if scope is None:
        return literal_sweep(command, cwd)
    res = []
    for text in scope:
        res.extend(literal_sweep(text, cwd, runnable=False))
    return res


def written_paths(command, cwd):
    """Абсолютные пути, которые вызов оболочки правит: разрешённые цели плюс, при неразрешимой
    цели, литералы связанной с ней области. Порядок проходов вызывающему не важен — потребителю,
    которому он важен (первым назвать разрешённую цель), звать scan_command и sweep порознь."""
    found, unresolved, eff_cwd, stripped = scan_command(command, cwd)
    res = [p for p, _ in found]
    if unresolved:
        res.extend(sweep(stripped, eff_cwd, unresolved))
    return res
