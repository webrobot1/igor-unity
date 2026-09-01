# Ведомая копия. Источник — /var/www/html/game/.claude/hooks/lib/chain_parser.py в серверном
# репозитории; копию перезаписывает его хук при каждой правке источника. Правка копии теряется
# молча — вносить её в источник. Серверного репозитория нет под рукой → назвать нужную правку
# пользователю.

"""Разбор shell-цепочки команд — общий носитель командных гейтов каталога: разрезают команду на
части, токенизируют часть, находят в части позицию имени команды, берут имя из токена.
ФОРМА ЗАПИСИ вызова разбирается тут же и одна на все гейты: продолженная переносом строка — часть
той же команды, тело heredoc — команды либо данные по своей заголовочной строке. Своя обработка
формы у гейта расходится с соседним молча: одну и ту же команду один отбивает, другой пропускает.
Каждый хук импортирует отсюда (sys.path.insert на свой каталог + `from chain_parser import ...`),
вместо вручную синхронизируемых копий у каждого. Расхождение с этим источником (собственное
определение вместо импорта) ловит .claude/hooks/command-guard.test.sh — там же перечень хуков,
обязанных импортировать."""

import re
import shlex

CHAIN = ';|&\n()'
ASSIGN = re.compile(r'^[A-Za-z_][A-Za-z0-9_]*=')
SHELLS = {'sh', 'bash', 'zsh', 'dash', 'ksh'}
INTERPRETERS = {'python', 'python3', 'perl', 'php', 'node', 'ruby'}
INLINE_FLAGS = {'-c', '-e', '-r', '--eval'}
# Заголовок heredoc: `<<EOF`, `<<-EOF`, `<<'EOF'`, `<<"EOF"`. Here-string (`<<<`) сюда не попадает —
# за `<<` там стоит `<`, а не имя делимитра.
HEREDOC = re.compile(r'<<-?\s*(["\']?)([A-Za-z_][A-Za-z0-9_]*)\1')
REDIR_FULL = re.compile(r'^(?:\d+|&)?(?:>>?|>\|)$')
REDIR_HEAD = re.compile(r'^(?:\d+|&)?(?:>>?|>\|)(?=[^>|])')
# Глубина разбора тел heredoc, вложенных друг в друга.
HEREDOC_DEPTH = 3
# Опции sudo/doas, забирающие следующий токен значением: пропустить оба, иначе значение
# прочлось бы командой.
SUDO_VALUE_OPTS = {'-u', '-g', '-p', '-C', '-U', '-r', '-t', '-h', '--user', '--group', '--prompt'}
# Ключевые слова оболочки и обёртки без собственных опций-значений. Часть цепочки после разреза
# начинается с них (`do rm …`, `then git reset …`, `nohup rm …`), и без пропуска именем команды
# прочлось бы само ключевое слово: гейт молчит на вызове, записанном телом цикла либо ветвью
# условия, — тем же вызове, ради которого он заведён.
LEADING = {'do', 'then', 'else', 'elif', '!', '{', 'if', 'while', 'until', 'time', 'command',
           'builtin', 'exec', 'nohup', 'nice'}
# Обёртки, за которыми стоит НАСТОЯЩАЯ команда, и опции каждой, забирающие следующий токен
# значением: без пропуска значения оно прочлось бы командой.
WRAPPER_VALUE_OPTS = {
    'sudo': SUDO_VALUE_OPTS,
    'doas': SUDO_VALUE_OPTS,
    # `env -u VAR cmd` снимает переменную окружения перед запуском: без пропуска ЗНАЧЕНИЯ именем
    # команды читается имя переменной, и гейт молчит на вызове под обёрткой — ровно на той форме,
    # которой снимают переменную-защиту перед запуском.
    'env': {'-u', '--unset', '-C', '--chdir'},
    'xargs': {'-n', '-P', '-I', '-i', '-s', '-d', '-E', '-L', '--max-args', '--max-procs',
              '--replace', '--delimiter', '--max-lines'},
    # Обёртка запуска команд проекта (`bin/run <команда>`): выбирает контур исполнения и зовёт
    # переданное как есть. Своих опций у неё нет — команда стоит первым же токеном за ней.
    # Без пропуска обёртки именем команды читается она сама, и КАЖДЫЙ гейт этого каталога молчит
    # на вызове под ней — ровно на той форме, которой команды проекта и запускаются.
    'run': set(),
}


def _bare(token):
    """Токен без кавычек и хвоста разреза цепочки (`/путь/x.md)` из `$(… /путь/x.md)`)."""
    return token.strip('"\'').strip('();,')


def _raw_parts(command):
    """Разрез цепочки на команды, ЧТУЩИЙ кавычки: разделитель внутри строки — данные, не граница.
    Иначе команда, несущая чужой текст аргументом (`sed -i "s|A|B|"`, `grep -E "a|b"`, сборка ТЗ,
    тест-набор), прочлась бы вызовом, которым не является. Части возвращаются стрипнутыми,
    пустые отфильтрованы.
    `\\` перед переносом строки границы не даёт: оболочка склеивает такие строки в одну команду.
    Без склейки перенос доезжает до токенизатора и встаёт в части ОТДЕЛЬНЫМ токеном, а дальше
    цена его двусторонняя: гейт, читающий цели, берёт его целью без абсолютного пути — отказ
    вызову, у которого все цели полные; гейт, читающий подкоманду по позиции, получает его на
    месте подкоманды — запрещённая подкоманда проходит молча."""
    parts, buf, quote, i, n = [], [], None, 0, len(command)
    while i < n:
        ch = command[i]
        if ch == '\\' and i + 1 < n and command[i + 1] == '\n' and quote != "'":
            i += 2
            continue
        if quote:
            buf.append(ch)
            if ch == '\\' and quote == '"' and i + 1 < n:
                buf.append(command[i + 1])
                i += 2
                continue
            if ch == quote:
                quote = None
            i += 1
            continue
        if ch == '\\' and i + 1 < n:
            buf.append(ch)
            buf.append(command[i + 1])
            i += 2
            continue
        if ch in '"\'':
            quote = ch
            buf.append(ch)
            i += 1
            continue
        if ch in CHAIN:
            parts.append(''.join(buf))
            buf = []
            i += 1
            continue
        buf.append(ch)
        i += 1
    parts.append(''.join(buf))
    return [p for p in (part.strip() for part in parts) if p]


def tokens(part):
    try:
        return shlex.split(part)
    except ValueError:
        return part.split()


def name(token):
    return token.lstrip('\\').rsplit('/', 1)[-1]


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
    for part in _raw_parts(header):
        if not HEREDOC.search(part):
            continue
        toks = strip_redirects(tokens(part))
        i = command_index(toks)
        if i >= len(toks):
            continue
        cmd = name(_bare(toks[i]))
        if not _stdin_script(toks[i + 1:]):
            continue
        if cmd in SHELLS:
            return 'shell'
        if cmd in INTERPRETERS:
            return 'code'
    return None


def split_heredocs(command):
    """Команда без тел heredoc и сами тела, поданные на ИСПОЛНЕНИЕ: (текст, [(вид, тело)]).
    Тело-данные — содержимое файла, не команда: строка `> цитата` в нём целью редиректа не
    является, и в перечень тел оно не идёт. Редирект заголовочной строки остаётся.
    Маркер `<<DELIM` с заголовка снимается вместе с телом: разбор идёт по одной команде дважды —
    потребитель зовёт strip_heredocs, а split_parts разбирает форму заново, — и на втором проходе
    оставленный маркер делимитра уже не находит, забирая телом ВЕСЬ хвост команды: вызов за
    heredoc уходит мимо гейта молча."""
    if '<<' not in command:
        return command, []
    lines = command.split('\n')
    kept, bodies, i = [], [], 0
    while i < len(lines):
        m = HEREDOC.search(lines[i])
        kind = _heredoc_kind(lines[i]) if m else None
        kept.append(lines[i][:m.start()] + lines[i][m.end():] if m else lines[i])
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
    return split_heredocs(command)[0]


def heredoc_bodies(command):
    return split_heredocs(command)[1]


def split_parts(command, depth=0):
    """Части цепочки команд. Тело heredoc, поданное чужой команде данными (`cat > файл <<EOF`,
    ввод `sed`), частью не является: текст в нём остаётся текстом, и вызов, ПРИВЕДЁННЫЙ в нём
    примером, командой не считается ни одним гейтом. Тело, поданное оболочке (`bash <<SH`),
    разбирается своей цепочкой — оно исполняется, и гейт обязан видеть его вызовы. Тело, поданное
    интерпретатору (`python3 - <<PY`), частями не даёт вовсе — это код, не цепочка; кому он нужен,
    берёт его heredoc_bodies и разбирает как код."""
    text, bodies = split_heredocs(command)
    parts = _raw_parts(text)
    if depth < HEREDOC_DEPTH:
        for kind, body in bodies:
            if kind == 'shell':
                parts.extend(split_parts(body, depth + 1))
    return parts


def command_index(toks, wrappers=WRAPPER_VALUE_OPTS):
    """Индекс токена с ИМЕНЕМ команды в части цепочки: перед ним стоят присваивания, ключевые
    слова оболочки (LEADING) и обёртки со своими опциями-значениями. len(toks) — команды в части
    нет. Хвост разреза цепочки (`/путь/x.md)` из `$(… /путь/x.md)`) с имени снимается: часть
    токенизируют и в обход split_parts."""
    i = 0
    while i < len(toks):
        t = toks[i]
        if ASSIGN.match(t) or t in LEADING:
            i += 1
            continue
        wrapper = wrappers.get(name(_bare(t)))
        if wrapper is None:
            break
        i += 1
        while i < len(toks):
            x = toks[i]
            if ASSIGN.match(x):
                i += 1
            elif x.startswith('-'):
                i += 2 if x in wrapper else 1
            else:
                break
    return i
