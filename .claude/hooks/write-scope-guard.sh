#!/bin/bash

# Ведомая копия. Источник — /var/www/html/game/.claude/hooks/write-scope-guard.sh в серверном
# репозитории; копию перезаписывает его хук при каждой правке источника. Правка копии теряется
# молча — вносить её в источник. Серверного репозитория нет под рукой → назвать нужную правку
# пользователю.

# PreToolUse hook (Bash, Edit, Write, NotebookEdit): запись файла ВНЕ каталогов проекта отклоняется
# — командой оболочки и файловым тулом наравне.
# Повод механического гейта: решение пользователя — «я считаю что тебе должно тока окружение
# папок проекта доступно быть». Bash, Edit и Write в проекте стоят в allow целиком,
# развилки-подтверждения перед выполнением не возникает, а временные файлы уезжали в `/tmp` и чужие
# каталоги: текстом правила класс не удержан. Текст правила — core «Файлы»; хук лишь не пропускает
# вызов при забытом правиле. Правка файла вне проекта по просьбе пользователя — тот же отказ:
# исключения хук не знает, такую правку вносит сам пользователь (его решение).
#
# КАНАЛОВ записи два, граница ОДНА — оттого обе ветки живут в одном файле: копия множества
# разрешённых каталогов во втором хуке разошлась бы с этой молча.
#   Bash — цели разбираются из самой команды (общий носитель разбора ниже);
#   Edit|Write|NotebookEdit — путь берётся готовым из поля вызова: `tool_input.file_path`, у
#     NotebookEdit — `tool_input.notebook_path`; подстановки в нём нет, вердикт по нему точен.
# Политика отказа разбора по каналам РАЗНАЯ, как у соседнего гейта владения путями (skill-guard.sh):
# файловый тул — fail-closed (одна правка, отказ дёшев, границу установить нечем); Bash — fail-open
# с уведомлением: хук висит на КАЖДОМ вызове оболочки, отказ разбора не смеет ронять работу.
#
# РАЗРЕШЁННОЕ множество каталогов берётся из носителей, не литералами:
#   корень проекта — два уровня над каталогом самого хука. У копии хука в другом репозитории это
#     его корень: литерал тут не нужен, и зеркалирование тела не правит. В сессии клиента либо узла
#     множество тем самым сужается до их репозитория и дерева его сессий — принято пользователем;
#   `permissions.additionalDirectories` из `.claude/settings.json` и `.claude/settings.local.json`
#     проекта — каждый каталог вместе с РЕПОЗИТОРИЕМ, которому он принадлежит (ближайший `.git`
#     вверх). Объявленный каталог у́же контура: сессия пишет и в канон, план и настройки контура,
#     лежащие выше него (knowledge-place «Правки конфигурации проекта»; план работы, названный
#     CLAUDE.md, лежит в репозитории клиента);
#   реальные каталоги за симлинками бандлов узла (`Build/*`) и за каталогом артефактов
#     `.playwright-mcp`, когда тот симлинк;
#   каталоги сессий проекта — `<tmp>/claude-<uid>/<слаг корня проекта>/` (там scratchpad сессии;
#     слаг — путь, где всё, кроме букв и цифр, заменено на `-`) — и то же дерево у слага из
#     `transcript_path` события: сессия, запущенная из чужого каталога, ведёт своё дерево под его
#     слагом. Дерево берётся целиком, не каталог одной сессии: субагенты запускаются ИЗ
#     scratchpad родителя и пишут в него относительными путями.
# Каталог, лежащий внутри другого разрешённого, из перечня отказа снимается: границу он не меняет.
#
# ЦЕЛИ записи разбирает общий носитель lib/write_targets.py: какие формы он ловит (редирект,
# cp/mv/mkdir/touch/install/tee, sed -i, вывод по параметру и позиции у curl/wget/ffmpeg/mktemp/
# tar/unzip/git clone, код интерпретатора инлайн и телом heredoc) и что проходит мимо, объявлено
# там; форму записи вызова (разрез цепочки, продолжение строки, тела heredoc) держит
# lib/chain_parser.py под ним. Здесь живёт только ГРАНИЦА: множество разрешённых каталогов и
# вердикт по абсолютному пути. Удаление вне проекта — та же правка чужого каталога: `rm` идёт под
# тем же вердиктом. `/dev/*` целью не считается (тот же носитель разбора).
# Что проходит МИМО, осознанно: цель из подстановки, кроме `~` и `$HOME` (значение приходит извне,
# и отказ был бы ложным); путь внутри контейнера за `docker …` (чужая файловая система); запись
# запущенным скриптом либо чужим инструментом без литерала пути. Ложное срабатывание дороже
# пропуска: хук висит на КАЖДОМ вызове оболочки.
# Дешёвого фильтра до python здесь нет: конверт события всегда несёт пути (`cwd`, `transcript_path`),
# а форм записи столько, что фильтр по их словам отсекал бы разве что пустую команду.
# Вход не разобран (нет python3, сломанный JSON): у Bash вызов НЕ блокируется, инжектится строка о
# непройденной проверке; у файлового тула — отказ (политика по каналам — выше). Отказ — обоими
# каналами разом: JSON permissionDecision и код 2 со stderr (skill `gate-mechanics`).
# Правится множество разрешённых каталогов, перечень перехватываемых тулов, разбор входа либо текст
# отказа — прогнать набор:
# /var/www/html/game/.claude/hooks/write-scope-guard.test.sh

input=$(cat)
hooks="$(cd "$(dirname "$0")" 2>/dev/null && pwd)"

if ! command -v python3 >/dev/null 2>&1; then
  if printf '%s' "$input" | grep -q '"tool_name"[[:space:]]*:[[:space:]]*"Bash"'; then
    echo '{"hookSpecificOutput":{"hookEventName":"PreToolUse","additionalContext":"write-scope-guard: граница записи не проверена — в окружении нет python3. Вызов не блокирован. Проверь сам: файл пишется только внутрь каталогов проекта (корень проекта, каталоги permissions.additionalDirectories и их репозитории, каталог сессии) — и командой оболочки, и файловым тулом; артефакт проверки и сгенерированный материал — в .playwright-mcp проекта, служебный файл сессии — в её каталог (scratchpad); файл вне проекта правит пользователь."}}'
    exit 0
  fi
  echo "[write-scope-guard] в окружении нет python3 — проверить границу записи нечем: правка отклонена" >&2
  exit 2
fi

HOOK_INPUT="$input" HOOKS_DIR="$hooks" python3 - <<'PY'
import json, os, re, sys, tempfile

HOOKS_DIR = os.path.realpath(os.environ.get("HOOKS_DIR") or ".")
sys.path.insert(0, os.path.join(HOOKS_DIR, "lib"))
from write_targets import normalize, scan_command

PROJ = os.path.realpath(os.path.join(HOOKS_DIR, "..", ".."))
SETTINGS = ("settings.json", "settings.local.json")
# Файловые тулы с готовым путём в поле вызова: у Edit и Write — `file_path`, у NotebookEdit —
# `notebook_path`. Правится парно с matcher регистрации хука — в `.claude/settings.json` проекта и в
# реестрах контуров, куда хук зеркалируется.
FILE_TOOLS = ("Edit", "Write", "NotebookEdit")
HOME_VAR = re.compile(r"\$\{HOME\}|\$HOME(?![A-Za-z0-9_])")

# Проза правила — для ветви неразобранного входа и текста отказа: в момент отказа свод адресату может
# быть не загружен ни одним каналом. Копий прозы две — вторая в bash-ветви выше без python.
BRIEF = ("файл пишется только внутрь каталогов проекта (корень проекта, каталоги "
         "permissions.additionalDirectories и их репозитории, каталог сессии) — и командой оболочки, "
         "и файловым тулом; артефакт проверки и сгенерированный материал — в `.playwright-mcp` "
         "проекта, служебный файл сессии — в её каталог (scratchpad); файл вне проекта правит "
         "пользователь")
RULE = ("Решение пользователя: «я считаю что тебе должно тока окружение папок проекта доступно "
        "быть» — core «Файлы».")


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
    sys.stderr.write("[write-scope-guard] " + reason + "\n")
    sys.exit(2)


def settings_dirs():
    """`permissions.additionalDirectories` из настроек проекта, обоих файлов."""
    dirs = []
    for fname in SETTINGS:
        try:
            with open(os.path.join(PROJ, ".claude", fname), encoding="utf-8") as fh:
                data = json.load(fh)
        except (OSError, ValueError):
            continue
        for d in ((data.get("permissions") or {}).get("additionalDirectories") or []):
            if isinstance(d, str) and d:
                dirs.append(os.path.normpath(os.path.expanduser(d)))
    return dirs


def repo_of(path):
    """Корень репозитория, которому принадлежит путь: ближайший каталог вверх с `.git` (каталог
    либо файл у рабочего дерева). None — не в репозитории либо каталог не смонтирован."""
    cur = path
    while True:
        if os.path.exists(os.path.join(cur, ".git")):
            return cur
        up = os.path.dirname(cur)
        if up == cur:
            return None
        cur = up


def links():
    """Реальные каталоги за симлинками бандлов узла и каталога артефактов."""
    res = []
    build = os.path.join(PROJ, "Build")
    try:
        names = [os.path.join(build, n) for n in os.listdir(build)]
    except OSError:
        names = []
    names.append(os.path.join(PROJ, ".playwright-mcp"))
    for p in names:
        if os.path.islink(p):
            res.append(os.path.realpath(p))
    return res


def slug(path):
    return re.sub(r"[^A-Za-z0-9]", "-", path)


def session_trees(data):
    """Деревья сессий: по слагу корня проекта и по слагу из пути транскрипта события."""
    base = os.path.join(tempfile.gettempdir(), "claude-%d" % os.getuid())
    res = [os.path.join(base, slug(PROJ))]
    parts = (data.get("transcript_path") or "").split("/")
    if "projects" in parts:
        k = parts.index("projects")
        if k + 1 < len(parts) and parts[k + 1]:
            res.append(os.path.join(base, parts[k + 1]))
    return res


def inside(path, roots):
    return any(path == r or path.startswith(r + "/") for r in roots)


def allowed_roots(data):
    res = [PROJ]
    for d in settings_dirs():
        res.append(d)
        top = repo_of(d)
        if top:
            res.append(top)
    res += links()
    res += session_trees(data)
    roots = []
    for r in res:
        r = r.rstrip("/") or "/"
        if r not in roots:
            roots.append(r)
    # Каталог внутри другого разрешённого границы не меняет — в перечне отказа он лишний.
    return [r for r in roots if not inside(r, [o for o in roots if o != r])]


def expand_home(raw):
    """`~` и `$HOME` в цели — домашний каталог пользователя оболочки: подстановка известна и без
    исполнения. Прочая подстановка неразрешима — такая цель пропускается."""
    home = os.path.expanduser("~")
    t = raw
    if t == "~" or t.startswith("~/"):
        t = home + t[1:]
    return HOME_VAR.sub(home, t)


raw_input = os.environ.get("HOOK_INPUT", "")
try:
    data = json.loads(raw_input)
except Exception as e:
    if re.search(r'"tool_name"\s*:\s*"Bash"', raw_input):
        out({"additionalContext": "write-scope-guard: вход хука не разобран (%s) — граница записи не "
                                  "проверена. Вызов не блокирован. Проверь сам: %s." % (e, BRIEF)})
    deny("вход хука не разобран (%s) — проверить границу записи нечем. %s." % (e, BRIEF))

tool = data.get("tool_name") or ""
tool_input = data.get("tool_input") or {}
cwd = data.get("cwd") or PROJ

if tool == "Bash":
    command = tool_input.get("command") or ""
    if not command:
        sys.exit(0)
    try:
        roots = allowed_roots(data)
        found, unresolved, eff_cwd, _stripped = scan_command(command, cwd)
        bad = [(path, fragment) for path, fragment in found if not inside(path, roots)]
        for raw, fragment in unresolved:
            if not raw:
                continue
            t = expand_home(raw)
            if "$" in t or "`" in t or t.startswith("~"):
                continue       # значение приходит извне: что будет записано, до исполнения неизвестно
            path = normalize(t, eff_cwd)
            if not inside(path, roots):
                bad.append((path, fragment))
    except Exception as e:
        out({"additionalContext": "write-scope-guard: команда не разобрана (%s). Вызов не блокирован. "
                                  "Проверь сам: %s." % (e, BRIEF)})
elif tool in FILE_TOOLS:
    raw = tool_input.get("file_path") or tool_input.get("notebook_path") or ""
    if not isinstance(raw, str) or not raw:
        sys.exit(0)            # без пути тул не пишет ничего — отказывает сам
    try:
        roots = allowed_roots(data)
        path = normalize(expand_home(raw), cwd)
    except Exception as e:
        deny("путь правки не разобран (%s) — проверить границу записи нечем. %s." % (e, BRIEF))
    bad = [] if inside(path, roots) else [(path, "%s %s" % (tool, raw))]
else:
    sys.exit(0)

if not bad:
    sys.exit(0)

seen, items = [], []
for path, fragment in bad:
    if path in seen:
        continue
    seen.append(path)
    frag = " ".join((fragment or "").split())
    if len(frag) > 120:
        frag = frag[:120] + "…"
    items.append("`%s` (%s)" % (path, frag))
    if len(items) == 4:
        break

deny("Отклонено правилом проекта: запись вне каталогов проекта — " + "; ".join(items) + ". " + RULE
     + " Разрешённые каталоги: " + ", ".join("`%s`" % r for r in roots)
     + " (каталог внутри них — тоже). Артефакт проверки и сгенерированный материал — в "
       "`.playwright-mcp` проекта, служебный файл сессии — в её каталог (scratchpad)"
     + (", `mktemp` — с `-p <каталог сессии>`" if tool == "Bash" else "")
     + "; файл вне проекта правит пользователь — назвать ему нужную правку.")
PY
