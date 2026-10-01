# Ведомая копия. Источник — /var/www/html/game/.claude/hooks/lib/shared_chain_parser.py в
# серверном репозитории; копию перезаписывает его хук при каждой правке источника. Правка копии
# теряется молча — вносить её в источник. Серверного репозитория нет под рукой → назвать нужную
# правку пользователю.

"""Разбор shell-цепочки команд — общий носитель командных гейтов каталога: разрезают команду на
части, токенизируют часть, находят в части позицию имени команды, берут имя из токена.
ФОРМА ЗАПИСИ вызова разбирается тут же и одна на все гейты: продолженная переносом строка — часть
той же команды, тело heredoc — команды либо данные по своей заголовочной строке. Своя обработка
формы у гейта расходится с соседним молча: одну и ту же команду один отбивает, другой пропускает.
Каждый хук импортирует отсюда (sys.path.insert на свой каталог + `from shared_chain_parser import ...`),
вместо вручную синхронизируемых копий у каждого. Расхождение с этим источником (собственное
определение вместо импорта) ловит .claude/hooks/shared-command-guard.test.sh: там перечень функций
носителя, а сверка идёт по всем хукам и модулям каталога."""

import re
import shlex

import shared_project_conf as project_conf

CHAIN = ';|&\n()'
# Разделитель в паре части цепочки, когда `&` либо `|` — знак перенаправления (`2>&1`, `&>файл`,
# `>|файл`): разрез по нему идёт, как по прочим, но ни подоболочки, ни фона он не значит.
REDIRECT_SEP = '>'
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
           'builtin', 'exec', 'nohup', 'setsid'}
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
    # Обёртки времени и приоритета исполнения (`timeout 300s cmd`, `nice -n 10 cmd`): без пропуска
    # именем команды читается обёртка либо её значение, и гейт молчит.
    'timeout': {'-s', '--signal', '-k', '--kill-after'},
    'nice': {'-n', '--adjustment'},
    'ionice': {'-c', '--class', '-n', '--classdata', '-p', '--pid'},
    'stdbuf': {'-i', '--input', '-o', '--output', '-e', '--error'},
    # Обёртка запуска команд проекта (`<корень>/<RUN_WRAPPER> <команда>`, ключ `.claude/project.conf`):
    # выбирает контур исполнения и зовёт переданное как есть. Своих опций у неё нет — команда стоит
    # первым же токеном за ней. Без пропуска обёртки именем команды читается она сама, и КАЖДЫЙ гейт
    # этого каталога молчит на вызове под ней — ровно на той форме, которой команды проекта и запускаются.
    'run': set(),
}
# Обёртки, у которых за опциями стоят позиционные аргументы самой обёртки (длительность у `timeout`),
# и лишь затем команда: число таких аргументов.
WRAPPER_POSITIONALS = {'timeout': 1}
# Обёртки контейнера: `docker compose exec [опции] <служба> <команда>`, `docker-compose exec …`,
# `docker exec [опции] <контейнер> <команда>`, `docker run [опции] <образ> <команда>`, те же формы
# через `docker container …` и у `podman`. За подкомандой идут опции, ОДНА позиционная (служба,
# контейнер либо образ) и лишь затем настоящая команда. Без пропуска именем команды читается сама
# утилита, и каждый гейт каталога молчит на вызове под обёрткой: `docker compose exec php git reset
# --hard` снимал рабочее дерево мимо гейта — каталог проекта смонтирован в контейнер.
# Форма без подкоманды exec/run (`docker compose ps`, `docker compose logs php`) обёрткой не
# является: команда там — сама утилита.
CONTAINER_ENGINES = {'docker', 'podman'}
CONTAINER_COMPOSERS = {'docker-compose', 'podman-compose'}
# Слово между движком и подкомандой: `docker compose exec`, `docker container exec`.
CONTAINER_GROUPS = {'compose', 'container'}
CONTAINER_SUBCOMMANDS = {'exec', 'run'}
# Опции движка и compose до подкоманды, забирающие следующий токен значением.
CONTAINER_GLOBAL_VALUE_OPTS = {'-f', '--file', '-p', '--project-name', '--env-file', '--profile',
                               '--project-directory', '--ansi', '--progress', '--parallel',
                               '-c', '--context', '-H', '--host', '--config', '-l', '--log-level'}
# Опции exec/run, забирающие следующий токен значением: без пропуска значение прочлось бы
# позиционной (службой), а служба — командой.
CONTAINER_VALUE_OPTS = {'-u', '--user', '-w', '--workdir', '-e', '--env', '--env-file', '--index',
                        '-v', '--volume', '-p', '--publish', '--name', '--entrypoint', '-l',
                        '--label', '--label-file', '--network', '--net', '--mount', '--detach-keys',
                        '--platform', '-m', '--memory', '--cpus', '-c', '--cpu-shares', '-h',
                        '--hostname', '--add-host', '--device', '--gpus', '--restart', '--pull',
                        '--ipc', '--pid', '--cap-add', '--cap-drop', '--security-opt', '--tmpfs',
                        '--ulimit', '--log-driver', '--log-opt', '--volumes-from', '--link', '--dns',
                        '--shm-size', '--stop-signal', '--cidfile', '--expose', '--group-add',
                        '--runtime'}


def _bare(token):
    """Токен без кавычек и хвоста разреза цепочки (`/путь/x.md)` из `$(… /путь/x.md)`)."""
    return token.strip('"\'').strip('();,')


def _subst_close(command, j):
    """Индекс парной `)` подстановки `$(`, чьё тело начинается в j; len(command) — пары нет. Тело держит
    свои кавычки и вложенные подстановки: кавычка внутри него внешнюю строку не закрывает."""
    depth, quote, n = 1, None, len(command)
    while j < n:
        ch = command[j]
        if ch == '\\' and quote != "'" and j + 1 < n:
            j += 2
            continue
        if quote:
            if ch == quote:
                quote = None
            elif quote == '"' and ch == '$' and j + 1 < n and command[j + 1] == '(':
                j = min(_subst_close(command, j + 2) + 1, n)
                continue
            j += 1
            continue
        if ch in '"\'':
            quote = ch
        elif ch == '(':
            depth += 1
        elif ch == ')':
            depth -= 1
            if depth == 0:
                return j
        j += 1
    return n


def _emit(links, part, sep, nested):
    """Часть с разделителем за ней, следом — тела подстановок `$(`, стоявших в её двойных кавычках:
    их исполняет дочерняя оболочка, форма та же, что у тела heredoc в split_links."""
    links.append((part, sep))
    for body in nested:
        links.append(('', '('))
        links.extend(_raw_links(body))
        links.append(('', ')'))


def _raw_links(command):
    """Разрез цепочки на команды, ЧТУЩИЙ кавычки: разделитель внутри строки — данные, не граница.
    Иначе команда, несущая чужой текст аргументом (`sed -i "s|A|B|"`, `grep -E "a|b"`, сборка ТЗ,
    тест-набор), прочлась бы вызовом, которым не является.
    `\\` перед переносом строки границы не даёт: оболочка склеивает такие строки в одну команду.
    Без склейки перенос доезжает до токенизатора и встаёт в части ОТДЕЛЬНЫМ токеном, а дальше
    цена его двусторонняя: гейт, читающий цели, берёт его целью без абсолютного пути — отказ
    вызову, у которого все цели полные; гейт, читающий подкоманду по позиции, получает его на
    месте подкоманды — запрещённая подкоманда проходит молча.
    Возвращает пары (часть, разделитель ЗА ней): `&&`, `||`, `|`, `&`, `;`, `(`, `)`, перенос строки;
    у `&` и `|` перенаправления (`2>&1`, `&>файл`, `>|файл`) — REDIRECT_SEP, у последней части —
    пустая строка. Части стрипнуты, пустые НЕ отфильтрованы: разделитель за пустой частью (`(` в
    начале подоболочки) значит, в какой оболочке исполнится следующая часть.
    Подстановка `$(` внутри двойных кавычек — не данные: строка остаётся частью своей команды, а тело
    подстановки идёт за ней частями дочерней оболочки (_emit). Без этого кавычка внутри тела закрывала
    внешнюю строку, и счёт кавычек до конца команды шёл вразнобой."""
    links, buf, quote, i, n = [], [], None, 0, len(command)
    nested = []
    while i < n:
        ch = command[i]
        if ch == '\\' and i + 1 < n and command[i + 1] == '\n' and quote != "'":
            i += 2
            continue
        if quote == '"' and ch == '$' and i + 1 < n and command[i + 1] == '(':
            close = _subst_close(command, i + 2)
            nested.append(command[i + 2:close])
            buf.append(command[i:close + 1])
            i = close + 1
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
            sep, step = ch, 1
            if ch in '&|' and i + 1 < n and command[i + 1] == ch:
                sep, step = ch * 2, 2
            elif ch in '&|' and ((i > 0 and command[i - 1] in '<>')
                                 or (ch == '&' and i + 1 < n and command[i + 1] == '>')):
                sep = REDIRECT_SEP
            _emit(links, ''.join(buf).strip(), sep, nested)
            buf, nested = [], []
            i += step
            continue
        buf.append(ch)
        i += 1
    _emit(links, ''.join(buf).strip(), '', nested)
    return links


def _raw_parts(command):
    """Части цепочки без разделителей, пустые отфильтрованы."""
    return [p for p, _ in _raw_links(command) if p]


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


def split_links(command, depth=0):
    """Части цепочки команд, каждая с разделителем за ней (_raw_links), пустые не отфильтрованы.
    Тело heredoc, поданное чужой команде данными (`cat > файл <<EOF`, ввод `sed`), частью не
    является: текст в нём остаётся текстом, и вызов, ПРИВЕДЁННЫЙ в нём примером, командой не
    считается ни одним гейтом. Тело, поданное оболочке (`bash <<SH`), разбирается своей цепочкой —
    оно исполняется, и гейт обязан видеть его вызовы; его части идут за частями всей команды в паре
    `(` … `)`: исполняет тело дочерняя оболочка. Тело, поданное интерпретатору (`python3 - <<PY`),
    частями не даёт вовсе — это код, не цепочка; кому он нужен, берёт его heredoc_bodies и разбирает
    как код."""
    text, bodies = split_heredocs(command)
    links = _raw_links(text)
    if depth < HEREDOC_DEPTH:
        for kind, body in bodies:
            if kind == 'shell':
                links.append(('', '('))
                links.extend(split_links(body, depth + 1))
                links.append(('', ')'))
    return links


def split_parts(command, depth=0):
    """Части цепочки команд без разделителей, пустые отфильтрованы; состав — split_links."""
    return [p for p, _ in split_links(command, depth) if p]


def _container_wrapper(toks, i):
    """Индекс токена за обёрткой контейнера, стоящей в toks[i], — за её опциями и позиционной;
    None — это не она (см. CONTAINER_SUBCOMMANDS)."""
    w = name(_bare(toks[i]))
    if w not in CONTAINER_ENGINES and w not in CONTAINER_COMPOSERS:
        return None
    j, grouped = i + 1, w in CONTAINER_COMPOSERS
    while j < len(toks):
        if toks[j].startswith('-'):
            j += 2 if _takes_value(toks[j], CONTAINER_GLOBAL_VALUE_OPTS) else 1
        elif not grouped and _bare(toks[j]) in CONTAINER_GROUPS:
            grouped, j = True, j + 1
        else:
            break
    if j >= len(toks) or _bare(toks[j]) not in CONTAINER_SUBCOMMANDS:
        return None
    j += 1
    while j < len(toks) and toks[j].startswith('-'):
        j += 2 if _takes_value(toks[j], CONTAINER_VALUE_OPTS) else 1
    return min(j + 1, len(toks))


def _takes_value(token, opts):
    """Опция забирает значением СЛЕДУЮЩИЙ токен: она в перечне и значение не слито с ней через `=`
    (`--user=www-data`)."""
    return '=' not in token and token in opts


def command_index(toks, wrappers=WRAPPER_VALUE_OPTS):
    """Индекс токена с ИМЕНЕМ команды в части цепочки: перед ним стоят присваивания, ключевые
    слова оболочки (LEADING), обёртки со своими опциями-значениями и обёртки контейнера.
    len(toks) — команды в части нет. Хвост разреза цепочки (`/путь/x.md)` из `$(… /путь/x.md)`) с
    имени снимается: часть токенизируют и в обход split_parts."""
    return command_position(toks, wrappers)[0]


def command_position(toks, wrappers=WRAPPER_VALUE_OPTS):
    """(индекс имени команды — как command_index, стоит ли перед ним обёртка контейнера).
    Команда под обёрткой контейнера исполняется в его файловой системе: абсолютный путь у неё
    адресует контейнер, и как он соотносится с путём хоста, задаёт монтирование проекта — разбор
    этого не знает. Относительный путь идёт от рабочего каталога контейнера. Обёртка запуска команд
    проекта идёт обёрткой контейнера, когда проект это объявил (`RUN_IN_CONTAINER` в
    `.claude/project.conf`): такая обёртка путей хоста не переводит; переводящая — обычная обёртка."""
    i, container = 0, False
    run_path = project_conf.value('RUN_WRAPPER') if project_conf.value('RUN_IN_CONTAINER') else ''
    while i < len(toks):
        t = toks[i]
        if ASSIGN.match(t) or t in LEADING:
            i += 1
            continue
        if run_path and (_bare(t) == run_path or _bare(t).endswith('/' + run_path)):
            i, container = i + 1, True
            continue
        skipped = _container_wrapper(toks, i)
        if skipped is not None:
            i, container = skipped, True
            continue
        wrapper = wrappers.get(name(_bare(t)))
        if wrapper is None:
            break
        positionals = WRAPPER_POSITIONALS.get(name(_bare(t)), 0)
        i += 1
        while i < len(toks):
            x = toks[i]
            if ASSIGN.match(x):
                i += 1
            elif x.startswith('-'):
                i += 2 if x in wrapper else 1
            elif positionals:
                positionals, i = positionals - 1, i + 1
            else:
                break
    return i, container
