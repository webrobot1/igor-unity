#!/bin/bash

# Ведомая копия. Источник — /var/www/html/game/.claude/hooks/file-tool-guard.sh в серверном
# репозитории; копию перезаписывает его хук при каждой правке источника. Правка копии теряется
# молча — вносить её в источник. Серверного репозитория нет под рукой → назвать нужную правку
# пользователю.

# PreToolUse hook (Bash): чтение и правка ИСХОДНИКА проекта командой оболочки отклоняются — это
# работа файлового тула (`Read`, `Edit`, `Write`).
# Текст правила — `CLAUDE.md`, абзац «Исходник проекта … читается и правится файловым тулом»; хук
# лишь не пропускает вызов при забытом правиле. Пометка «правится парно» относится к паре
# `CLAUDE.md` ↔ этот файл: текст отказа ниже — осознанный дубль прозы правила, расходиться им нельзя.
# Правило стоит в каноне, а не в своде, оттого что спорит с инструкцией режима сессии: канон читает
# и классификатор режима, и всякий исполнитель, которому канон наследуется.
#
# Повод механического гейта: инструкция режима сессии («веди файловую работу через Bash, к
# Read/Edit/Write обращайся, только когда Bash не справляется») приходит системным сообщением и
# действует всю сессию, а текст правила держится вниманием — в сессии 06.09.2026 внимания не
# хватило дважды подряд, при живом правиле. Достижимость впредь: Bash в проекте стоит в allow
# целиком, развилки-подтверждения перед вызовом не возникает.
# Чем плоха оболочка на исходнике: чтение `cat`/`sed -n` не поднимает своды с масками `paths`
# (агент работает в домене без его правил) и не даёт `Edit` базовой версии, по которой тот ловит
# правку параллельной сессии; правка `sed -i`/heredoc исполняет написанное буквально — режет не тот
# кусок и затирает чужое без сигнала.
#
# ЧТО ловится:
#   чтение — `cat`, `head`, `tail`, `less`, `more`, `nl`, `bat`, `sed`, `awk` (файлы после программы) с
#     файлом-исходником; цикл `for` по литеральному перечню исходников, чья переменная идёт в команду
#     чтения тела (путь со строкой `путь:число` тоже); поиск
#     `grep`/`rg` по исходнику шаблоном, совпадающим с каждой строкой (`''`, `^`), без флагов счёта,
#     перечня файлов, тишины и обращения совпадения; открытие исходника на чтение кодом интерпретатора —
#     инлайн-флагом и телом heredoc (адреса отдаёт lib/write_targets.py, code_read_paths);
#   правка — in-place (`sed -i`, `perl -i`, `awk -i`), `tee`, редирект из `cat`/`echo`/`printf`
#     (создание файла руками), запись из кода интерпретатора — инлайн-флагом и телом heredoc.
# ЦЕЛИ записи разбирает общий носитель lib/write_targets.py, форму вызова (разрез цепочки,
# продолжение строки, тела heredoc) — lib/chain_parser.py под ним: своей обработки формы у гейта
# нет, она разошлась бы с соседним гейтом молча.
#
# Что проходит МИМО, осознанно:
#   поиск (`grep`, `rg`) содержательным шаблоном, метрики (`wc`, `ls`, `find`), проверки и команды
#     проекта (`php -l`, `node --check`, `bin/run …`) — файловый тул этого не делает; код
#     интерпретатора, поданный файлом-скриптом, — что он читает, из вызова не видно;
#   редирект вывода САМОЙ команды в файл (`bin/run php bin/console … > отчёт.md`) — правило прямо
#     оставляет такую запись оболочке; ловится лишь редирект из `cat`/`echo`/`printf`, где
#     содержимое пишет человек, а не команда;
#   не-исходник — журнал, артефакт прогона, зависимости, каталог сессии (перечень NOT_SOURCE);
#   удаление (`rm`, `unlink`, `shred`, `rmdir`) — правило о чтении и правке, а тула удаления нет
#     вовсе; опасные формы держит соседний гейт `rm-path-guard`;
#   цель из подстановки: значение приходит извне, отказ был бы ложным.
# Ложное срабатывание дороже пропуска: хук висит на КАЖДОМ вызове оболочки.
# Вход не разобран (нет python3, сломанный JSON) — вызов НЕ блокируется, инжектится строка о
# непройденной проверке: ронять работу отказ разбора не смеет. Отказ — обоими каналами разом: JSON
# permissionDecision и код 2 со stderr (skill `gate-mechanics`).
# Правится перечень команд, расширений, исключённых каталогов, разбор входа либо текст отказа —
# прогнать набор:
# /var/www/html/game/.claude/hooks/file-tool-guard.test.sh

input=$(cat)
hooks="$(cd "$(dirname "$0")" 2>/dev/null && pwd)"

BRIEF='исходник проекта (код, шаблон, конфигурация, markdown) читается через Read и правится через Edit/Write, не командой оболочки; оболочке остаются поиск, метрики, запуск проверок и работа с не-исходником'

if ! command -v python3 >/dev/null 2>&1; then
  echo "{\"hookSpecificOutput\":{\"hookEventName\":\"PreToolUse\",\"additionalContext\":\"file-tool-guard: канал работы с файлом не проверен — в окружении нет python3. Вызов не блокирован. Проверь сам: $BRIEF.\"}}"
  exit 0
fi

HOOK_INPUT="$input" HOOKS_DIR="$hooks" BRIEF="$BRIEF" python3 - <<'PY'
import json, os, re, sys, tempfile

HOOKS_DIR = os.path.realpath(os.environ.get("HOOKS_DIR") or ".")
sys.path.insert(0, os.path.join(HOOKS_DIR, "lib"))
from chain_parser import split_parts, tokens, command_index, name, strip_redirects, _bare
from write_targets import code_read_paths, normalize, scan_command

PROJ = os.path.realpath(os.path.join(HOOKS_DIR, "..", ".."))
BRIEF = os.environ.get("BRIEF", "")

# Команды, чей смысл — выложить содержимое файла. `sed` тут по обеим осям — и окно `-n`, и in-place.
READ_CMDS = {"cat", "head", "tail", "less", "more", "nl", "bat", "sed"}
# `awk` выкладывает файл так же, но первым позиционным аргументом берёт программу, а не файл; опции
# `-F`, `-v`, `-f` забирают значение. С `-f` программа приходит файлом, и позиционные — все файлы.
AWK_CMDS = {"awk", "gawk", "mawk", "nawk"}
AWK_VALUE_OPTS = {"-F", "-v", "-f", "--field-separator", "--assign", "--file"}
# Цикл по перечню исходников с командой чтения в теле: путь в теле приходит переменной цикла, и
# разбор одного вызова его не видит, хотя перечень записан литералом в самой команде.
FOR_LIST = re.compile(r"\bfor\s+([A-Za-z_]\w*)\s+in\s+(.*?)\s*(?:;|\n)\s*do\b(.*)", re.S)
# Поиск содержательным шаблоном локализует, а не читает, и файловый тул его не заменяет. Шаблон,
# совпадающий с каждой строкой, места не локализует: такой поиск выкладывает файл целиком, то есть
# читает. Флаги счёта, перечня файлов, тишины и обращения совпадения содержимого не выкладывают —
# с ними вызов остаётся поиском.
SEARCH_CMDS = {"grep", "egrep", "fgrep", "rg"}
MATCH_ALL = {"", "^", "$", ".", ".*", "^.*", "^.*$"}
NO_CONTENT_LONG = {"--count", "--count-matches", "--files-with-matches", "--files-without-match",
                   "--quiet", "--silent", "--files", "--invert-match"}
# Короткие флаги без выкладки и опции со значением у `rg` и `grep` свои: `-L` у `rg` — переход по
# ссылкам, у `grep` — перечень файлов без совпадений; `-E` у `rg` — кодировка со значением, у `grep` —
# расширенный шаблон без значения.
NO_CONTENT_SHORT = {"rg": set("clqv"), "grep": set("clLqv")}
SEARCH_VALUE_OPTS = {
    "rg": {"-g", "--glob", "--iglob", "-t", "--type", "-T", "--type-not", "-A", "--after-context",
           "-B", "--before-context", "-C", "--context", "-m", "--max-count", "-j", "--threads",
           "-M", "--max-columns", "-E", "--encoding", "-r", "--replace", "-d", "--max-depth",
           "--max-filesize", "--context-separator", "--field-match-separator", "--path-separator",
           "--pre", "--pre-glob", "--sort", "--sortr", "--type-add", "--type-clear", "--ignore-file",
           "--colors", "--color", "--engine"},
    "grep": {"-A", "--after-context", "-B", "--before-context", "-C", "--context", "-m",
             "--max-count", "-d", "--directories", "-D", "--devices", "--include", "--exclude",
             "--exclude-dir", "--exclude-from", "--label", "--group-separator"},
}
# Команды, чей редирект пишет НЕ вывод работы, а набранный человеком текст: файл создаётся руками.
# Прочие команды под редиректом законны — правило прямо оставляет оболочке запись, чьё содержимое
# есть вывод самой команды.
HAND_WRITE = {"cat", "echo", "printf"}
# Удаление правилом не покрыто: оно говорит о чтении и правке, а файлового тула удаления нет вовсе —
# отказ тут отрезал бы операцию, у которой не остаётся никакого канала. Опасные формы (цель без
# абсолютного пути, мелкая глубина, цель из подстановки) держит соседний гейт `rm-path-guard`.
DELETE_CMDS = {"rm", "unlink", "shred", "rmdir"}
# Исходник проекта по расширению. Расширения нет либо оно не тут — файл под правило не подпадает
# (данные, дамп, бинарник).
SRC_EXT = {".php", ".twig", ".js", ".mjs", ".cjs", ".ts", ".css", ".scss", ".json", ".yaml", ".yml",
           ".neon", ".xml", ".md", ".cs", ".lua", ".sh", ".sql", ".html", ".dist", ".tsx", ".jsx"}
# Не-исходники по МЕСТУ: журналы и кеш, зависимости, служебное дерево git, артефакты браузера,
# установленная копия ассетов бандлов. Каталог сессии добавляется отдельно — он вне проекта.
NOT_SOURCE = ("var/", "vendor/", "node_modules/", ".git/", ".playwright-mcp/", "public/bundles/")


def out(payload):
    print(json.dumps({"hookSpecificOutput": dict(hookEventName="PreToolUse", **payload)},
                     ensure_ascii=False))
    sys.exit(0)


def deny(reason):
    """Отказ обоими каналами разом: JSON permissionDecision и код 2 со stderr — skill
    `gate-mechanics`."""
    print(json.dumps({"hookSpecificOutput": {"hookEventName": "PreToolUse",
                                             "permissionDecision": "deny",
                                             "permissionDecisionReason": reason}},
                     ensure_ascii=False))
    sys.stderr.write("[file-tool-guard] " + reason + "\n")
    sys.exit(2)


def session_root():
    return os.path.join(tempfile.gettempdir(), "claude-%d" % os.getuid())


def is_source(path):
    """Путь — исходник проекта: лежит в дереве проекта, несёт расширение исходника и не попадает в
    не-исходное место."""
    if os.path.splitext(path)[1].lower() not in SRC_EXT:
        return False
    if path.startswith(session_root()):
        return False
    if path != PROJ and not path.startswith(PROJ + "/"):
        return False
    rel = path[len(PROJ) + 1:]
    return not any(rel.startswith(d) or ("/" + d) in ("/" + rel) for d in NOT_SOURCE)


def search_read_targets(args, kind, cwd):
    """Исходники, которые поиск выкладывает целиком: шаблон из MATCH_ALL и ни одного флага без
    выкладки содержимого. Шаблон — значение `-e`/`--regexp`, иначе первый позиционный аргумент;
    шаблоны из файла (`-f`) из вызова не видны — такой поиск не судится."""
    long_values = SEARCH_VALUE_OPTS[kind]
    short_values = {o[1] for o in long_values if len(o) == 2}
    patterns, from_file, plain, k = [], False, [], 0
    while k < len(args):
        a = args[k]
        if a == "--":
            plain += args[k + 1:]
            break
        if a.startswith("--"):
            opt, eq, val = a.partition("=")
            if opt in NO_CONTENT_LONG:
                return []
            if (opt in ("--regexp", "--file") or opt in long_values) and not eq:
                val = args[k + 1] if k + 1 < len(args) else ""
                k += 1
            if opt == "--regexp":
                patterns.append(val)
            from_file = from_file or opt == "--file"
            k += 1
            continue
        if a.startswith("-") and len(a) > 1:
            body = a[1:]
            for pos, ch in enumerate(body):
                if ch in NO_CONTENT_SHORT[kind]:
                    return []
                if ch in "ef" or ch in short_values:
                    val = body[pos + 1:]
                    if not val and k + 1 < len(args):
                        val = args[k + 1]
                        k += 1
                    if ch == "e":
                        patterns.append(val)
                    from_file = from_file or ch == "f"
                    break
            k += 1
            continue
        plain.append(a)
        k += 1
    if not patterns and not from_file and plain:
        patterns, plain = [plain[0]], plain[1:]
    if from_file or not any(p in MATCH_ALL for p in patterns):
        return []
    res = []
    for arg in plain:
        arg = _bare(arg)
        if not arg or "$" in arg or "`" in arg or "*" in arg:
            continue
        path = normalize(os.path.expanduser(arg), cwd)
        if is_source(path):
            res.append(path)
    return res


def awk_read_targets(args, cwd):
    """Исходники, которые `awk` читает: позиционные аргументы после программы (с `-f` — все),
    кроме присваиваний `имя=значение`; цель с подстановкой пропускается."""
    res, program_seen, k = [], False, 0
    while k < len(args):
        a = args[k]
        if a == "--":
            k += 1
            continue
        if a.startswith("-") and len(a) > 1:
            if a in ("-f", "--file") or a.startswith("--file=") or (a.startswith("-f") and not a.startswith("--")):
                program_seen = True
            if a in AWK_VALUE_OPTS:
                k += 1         # значение — следующим токеном; слитное (`-F:`, `--assign=x=1`) уже в токене
            k += 1
            continue
        if not program_seen:
            program_seen = True
            k += 1
            continue
        arg = _bare(a)
        k += 1
        if not arg or "$" in arg or "`" in arg or "*" in arg or re.match(r"^[A-Za-z_]\w*=", arg):
            continue
        path = normalize(os.path.expanduser(arg), cwd)
        if is_source(path):
            res.append(path)
    return res


def loop_read_targets(command, cwd):
    """Исходники перечня цикла `for`, чья переменная идёт в команду чтения тела: тело упоминает
    переменную и несёт команду чтения с подстановкой в аргументе. Перечень — литералы; слово с
    маской либо подстановкой пропускается, хвост `:число` (путь со строкой) снимается."""
    res = []
    for m in FOR_LIST.finditer(command):
        var, words, body = m.group(1), m.group(2), m.group(3)
        if not re.search(r"\$\{?" + var + r"\b", body):
            continue
        reads = False
        for part in split_parts(body):
            toks = strip_redirects(tokens(part))
            i = command_index(toks)
            if i < len(toks) and name(_bare(toks[i])) in READ_CMDS | AWK_CMDS \
                    and any("$" in t for t in toks[i + 1:]):
                reads = True
                break
        if not reads:
            continue
        for word in words.split():
            word = re.sub(r":\d+$", "", _bare(word))
            if not word or "$" in word or "`" in word or "*" in word:
                continue
            path = normalize(os.path.expanduser(word), cwd)
            if is_source(path):
                res.append((path, m.group(0)[:100]))
    return res


def read_targets(command, cwd):
    """Файлы-исходники, которые команда ЧИТАЕТ: позиционные аргументы команд чтения и поиска,
    выкладывающего файл целиком. Опции и их значения пропускаются, цель с подстановкой — тоже
    (значение приходит извне)."""
    res = []
    for part in split_parts(command):
        toks = strip_redirects(tokens(part))
        i = command_index(toks)
        if i >= len(toks):
            continue
        cmd = name(_bare(toks[i]))
        if cmd in SEARCH_CMDS:
            kind = "rg" if cmd == "rg" else "grep"
            res += [(path, part) for path in search_read_targets(toks[i + 1:], kind, cwd)]
            continue
        if cmd in AWK_CMDS:
            res += [(path, part) for path in awk_read_targets(toks[i + 1:], cwd)]
            continue
        if cmd not in READ_CMDS:
            continue
        for arg in toks[i + 1:]:
            arg = _bare(arg)
            if not arg or arg.startswith("-") or "$" in arg or "`" in arg or "*" in arg:
                continue
            path = normalize(os.path.expanduser(arg), cwd)
            if is_source(path):
                res.append((path, part))
    return res


def write_targets_src(command, cwd):
    """Цели ЗАПИСИ, попадающие под правило: исходник проекта, записанный формой правки.
    Различитель — РЕДИРЕКТ, не имя команды: под редиректом в файл уходит вывод работы команды, и
    такая запись правилом оставлена оболочке; исключение — `cat`/`echo`/`printf`, где содержимое
    набирает человек, то есть файл создаётся руками. Цель БЕЗ редиректа порождена самой формой
    правки — in-place, `tee`, запись из кода интерпретатора, — и идёт под отказ."""
    res = []
    found, _unresolved, _eff_cwd, _stripped = scan_command(command, cwd)
    for path, fragment in found:
        if not is_source(path):
            continue
        all_toks = tokens(fragment or "")
        toks = strip_redirects(all_toks)
        i = command_index(toks)
        cmd = name(_bare(toks[i])) if i < len(toks) else ""
        if cmd in DELETE_CMDS:
            continue           # удаление — не правка: файлового тула у него нет вовсе
        if cmd == "mv" or (cmd == "git" and "mv" in [_bare(t) for t in toks[i + 1:i + 4]]):
            if not os.path.exists(path):
                continue       # переименование в новый путь — не правка: файлового тула у него нет вовсе;
                               # цель существует — перезапись содержимого, отказ прежний
        if len(toks) != len(all_toks):
            if cmd not in HAND_WRITE:
                continue       # содержимое пишет сама команда — законная запись оболочкой
        res.append((path, fragment))
    return res


raw_input = os.environ.get("HOOK_INPUT", "")
try:
    data = json.loads(raw_input)
except Exception as e:
    out({"additionalContext": "file-tool-guard: вход хука не разобран (%s) — канал работы с файлом "
                              "не проверен. Вызов не блокирован. Проверь сам: %s." % (e, BRIEF)})

if (data.get("tool_name") or "") != "Bash":
    sys.exit(0)

command = (data.get("tool_input") or {}).get("command") or ""
if not command:
    sys.exit(0)
cwd = data.get("cwd") or PROJ

try:
    hits = (read_targets(command, cwd) + loop_read_targets(command, cwd) + write_targets_src(command, cwd)
            + [(path, frag) for path, frag in code_read_paths(command, cwd) if is_source(path)])
except Exception as e:
    out({"additionalContext": "file-tool-guard: команда не разобрана (%s). Вызов не блокирован. "
                              "Проверь сам: %s." % (e, BRIEF)})

if not hits:
    sys.exit(0)

seen, items = [], []
for path, fragment in hits:
    if path in seen:
        continue
    seen.append(path)
    frag = " ".join((fragment or "").split())
    if len(frag) > 100:
        frag = frag[:100] + "…"
    items.append("`%s` (%s)" % (path, frag))
    if len(items) == 4:
        break

deny("Отклонено правилом проекта: исходник проекта читают и правят файловым тулом, не командой "
     "оболочки — " + "; ".join(items) + ". Вместо этой команды: чтение — `Read` (окно параметрами "
     "offset/limit), правка — `Edit` с точным совпадением текста либо `Write` целиком. "
     "Оболочке остаются поиск (`grep`, `rg`), метрики (`wc`, `ls`), запуск проверок и команд "
     "проекта, работа с не-исходником (журнал, артефакт прогона, каталог сессии) и запись, чьё "
     "содержимое есть вывод самой команды. Правило — `CLAUDE.md`; указание режима сессии вести "
     "файловую работу оболочкой его не отменяет.")
PY
