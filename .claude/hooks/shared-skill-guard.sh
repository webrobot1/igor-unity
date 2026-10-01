#!/bin/bash

# Ведомая копия. Источник — /var/www/html/game/.claude/hooks/shared-skill-guard.sh в серверном
# репозитории; копию перезаписывает его хук при каждой правке источника. Правка копии теряется
# молча — вносить её в источник. Серверного репозитория нет под рукой → назвать нужную правку
# пользователю.

# PreToolUse hook: защищённый путь правит только агент-владелец (поле agent_type в stdin);
# из главной сессии (agent_type пуст) — отказ.
# Каналов правки ДВА, таблица владения ОДНА — оттого обе ветки живут в одном файле: копия
# таблицы во втором хуке разошлась бы с этой молча.
#   Edit|Write — путь берётся готовым из tool_input.file_path;
#   Bash       — путь разбирается из самой команды. Без этой ветки таблица обходится штатным
#                способом работы: среда сессий прямо предписывает править файлы командами
#                оболочки (sed -i, heredoc, cp/mv), и класс обходится не краем, а по умолчанию.
# Политика отказа разбора по каналам РАЗНАЯ: Edit|Write — fail-closed (одна правка, отказ дёшев,
# владельца установить нечем); Bash — fail-open с уведомлением, как у соседних Bash-хуков проекта:
# хук висит на КАЖДОМ вызове оболочки, отказ разбора не смеет ронять работу.
# Сам разбор команды — общий носитель lib/shared_write_targets.py: какие формы записи он ловит и что
# проходит мимо, объявлено там. Здесь живёт только таблица владения путями.
# Проектное в таблице — ключи `.claude/project.conf`: DOC_PATHS — пути документации проекта сверх
# общих (маски от корня, `**` — любая глубина), PLANS_DIR — каталог план-файлов, которые ведёт главная
# сессия.
# ВНЕШНИЕ КОНТУРЫ — репозитории, куда проект кладёт ведомые копии своих сводов и хуков, — объявляет
# проектный модуль lib/contours.py (ALL — корни контуров, SOURCE_ROOT — корень проекта-источника);
# модуля нет — контуров у проекта нет. У каждого контура свой корневой `CLAUDE.md`, и он не ведомая
# копия, а самостоятельный носитель: источника в проекте-источнике у него нет, правка идёт по месту.
# Рядом с ним в тех же репозиториях лежат ВЕДОМЫЕ копии сводов и скриптов, и правило у них обратное —
# правка копии теряется на следующей сверке зеркала, вносить её надо в источник. Различает их ПОМЕТКА
# ведомости, проектный модуль mirror_copy (lib, у проекта с контурами): имя и каталог тут не признак — каталоги у адресата общие
# с его собственными файлами, а имя копии совпадает с именем источника. Прочее содержимое внешних
# репозиториев не защищено вовсе: их README и код ведёт своя сторона. Реестр настроек контура под
# защитой наравне со здешним: им запускается копия ЭТОГО гейта у той стороны, и снятая регистрация
# гасит её молча — ни отказа, ни следа ни в одной из двух сессий.
# Корень проекта считается от расположения хука, у ведомой копии в контуре — берётся SOURCE_ROOT
# модуля контуров: своё расположение у копии лежит в чужом репозитории, и вычисленный из него корень
# назвал бы проектом-источником сам этот репозиторий. Тогда ветвь путей источника стала бы недостижимой
# (корень совпал бы с корнем контура, а тот проверяется раньше), а отказ на правке ведомой копии
# адресовал бы за источником её саму. Каталог `lib` остаётся от СВОЕГО расположения: модули копия
# импортирует свои; ключи project.conf читаются у проекта-источника.
#
# Маркеры `.last` протокола shared-fileagent — своё владение: носитель ведёт маркер СВОЕГО имени, чужой
# маркер отбивается ему наравне с прочими защищёнными путями. Пишется маркер командой оболочки
# (значение его есть вывод git-команды — skill `shared-fileagent`), потому владение по нему проверяет
# ветка Bash. Маркер зоны аудита ведёт главная сессия: пустой agent_type тут ВЛАДЕЛЕЦ, а не
# отсутствие проверки, и субагенту такой путь отбивается наравне с прочими.
#
# ПОСЛАБЛЕНИЕ главной сессии — точечная правка пути владельца: Edit, у которого `old_string` непуст,
# а `new_string` не длиннее POINT_EDIT_LINES строк, либо Write нового файла свода
# (`.claude/skills/<имя>/SKILL.md`, файла ещё нет) не длиннее POINT_EDIT_LINES строк. Мерится текст
# ЗАМЕНЫ: у вызова файлового тула он один ограничен по конструкции, а у команды оболочки (`sed -i`
# по всему файлу, редирект) такой меры нет вовсе — ветка Bash главную сессию отбивает по-прежнему.
# Write существующего файла и Edit с пустым `old_string` — вставка файла либо раздела. Вне послабления
# маркер `.last` (владение своё, абзац выше) и ведомая копия (владельца нет — правка любого
# размера теряется на сверке зеркала); реестр настроек главная сессия ведёт и без послабления.
# Серию точечных правок закрывает проверяющий проход владельца; текст отказа воспроизводит
# правило вместе с порогом — осознанный дубль: в момент отказа свод адресату не доезжает.
# Правится парно с shared-knowledge-place «Правки конфигурации проекта».

input=$(cat)

# Дешёвый фильтр до python: защищённые пути лежат среди markdown проекта и `.claude/**`. Ни того,
# ни другого в тексте вызова нет — защищённого пути в нём нет по конструкции. Регистр расширения
# фильтр не различает: документация проекта пишется и с `.MD`, а якорем зоны служит МЕСТО файла.
shopt -s nocasematch
case "$input" in
  *.md*|*.claude*) ;;
  *) exit 0 ;;
esac
shopt -u nocasematch

proj="$(cd "$(dirname "$0")/../.." 2>/dev/null && pwd)"
hooks="$(cd "$(dirname "$0")" 2>/dev/null && pwd)"
DOC_PATHS=""; PLANS_DIR=""
[ -f "$proj/.claude/project.conf" ] && . "$proj/.claude/project.conf"

# Перечень владельцев прозой — для fail-open ветви: вызов там не блокируется, а свод правил
# («Правки конфигурации проекта») у адресата инжекта может не быть загружен ни одним каналом.
# Копий прозы ДВЕ — вторая в python-части ниже, — и печатаются обе лишь тогда, когда проверка
# не состоялась: разъезд их не виден ни на одном рабочем вызове, оттого его ловит набор
# самопроверки. Сама проза с таблицей тоже сверяется набором: кейс «проза зоны владения ↔
# фактический вердикт гейта» разбирает её на классы путей и спрашивает вердикт по каждому.
# Без python ведомая копия в контуре корня источника не знает и называет значения своего project.conf.
brief_docs=""
read -r -a doc_paths <<< "$DOC_PATHS"
for g in "${doc_paths[@]}"; do brief_docs="$brief_docs, \`$g\`"; done
brief_plans=""
[ -n "$PLANS_DIR" ] && brief_plans="\`$PLANS_DIR/\` и "
BRIEF="\`.claude/agents|hooks|workflows\` правит главная сессия, \`.claude/skills\`, канон \`CLAUDE.md\`, markdown корня проекта, markdown \`.claude/**\` вне зон выше, \`docs/\`$brief_docs — shared-skill-editor, \`.claude/settings.json\` — главная сессия, маркер \`.claude/agents/<имя>.last\` — агент этого имени и главная сессия, маркер \`.claude/workflows/*.last\` — главная сессия; ${brief_plans}markdown вне перечисленных мест под защиту не подпадают. Корневой \`CLAUDE.md\` внешнего контура — канон внешнего контура — правит shared-skill-editor; настройки внешнего контура — главная сессия; ведомая копия свода либо скрипта в контуре не правится никем: правка идёт в источник."

# Проверка не состоялась — нет python3 либо разбор упал до своего вердикта (сломанный общий модуль,
# исключение вне перехвата: код 1 программа отказом не читает). Политика по каналам — шапка.
unchecked() { # unchecked <причина без кавычек>
  if printf '%s' "$input" | grep -q '"tool_name"[[:space:]]*:[[:space:]]*"Bash"'; then
    printf '{"hookSpecificOutput":{"hookEventName":"PreToolUse","additionalContext":"skill-guard: владение путём не проверено — %s. Вызов не блокирован. Проверь сам: %s Командой оболочки эта проверка не обходится."}}\n' "$1" "$BRIEF"
    exit 0
  fi
  local reason="[skill-guard] $1 — проверить владельца защищённого пути нечем: правка отклонена"
  printf '{"hookSpecificOutput":{"hookEventName":"PreToolUse","permissionDecision":"deny","permissionDecisionReason":"%s"}}\n' "$reason"
  echo "$reason" >&2
  exit 2
}

command -v python3 >/dev/null 2>&1 || unchecked "в окружении нет python3"

HOOK_INPUT="$input" PROJ="$proj" HOOKS_DIR="$hooks" python3 - <<'PY'
import fnmatch, json, os, re, sys

sys.path.insert(0, os.path.join(os.environ.get("HOOKS_DIR") or "", "lib"))
PROJ = (os.environ.get("PROJ") or "").rstrip("/")
# Внешние контуры и корень источника — проектный модуль (шапка хука); модуля нет — контуров нет.
try:
    import contours
    CONTOURS = tuple(r.rstrip("/") for r in getattr(contours, "ALL", ()) if r)
    PROJ = (getattr(contours, "SOURCE_ROOT", "") or PROJ).rstrip("/")
except ImportError:
    CONTOURS = ()
try:
    from mirror_copy import marked
except ImportError:
    def marked(path):
        return False
# Ключи project.conf — проекта-источника: у ведомой копии в контуре свой файл его значений не несёт.
os.environ["CLAUDE_PROJECT_CONF"] = os.path.join(PROJ, ".claude", "project.conf")
import shared_project_conf as project_conf
from shared_write_targets import normalize, scan_command, sweep

# Недоступный корень контура (диск не смонтирован, репозитория на машине нет) ветви не меняет —
# вердикт по пути считается по самому пути, а пометка ведомости у нечитаемого файла не находится и
# путь остаётся незащищённым: писать по такому пути всё равно нечем.

# Псевдо-владелец ведомой копии: реального владельца у неё нет — правка теряется у КАЖДОГО
# исполнителя, включая владельца сводов, и адресуется она не агенту, а источнику.
MIRROR = "\x00mirror"

# Порог послабления главной сессии на точечную правку (шапка хука): число строк текста ЗАМЕНЫ.
# Правится парно с shared-knowledge-place «Правки конфигурации проекта».
POINT_EDIT_LINES = 10

# Зона владельца сводов — НОСИТЕЛИ, названные shared-knowledge-place «Правки конфигурации проекта»: своды,
# канон `CLAUDE.md` (в корне либо в `.claude`), markdown корня, документация проекта и пути документации сверх общих (ключ
# DOC_PATHS). Расширением имени она не задаётся: markdown лежит и вне её — артефакт прогона в каталоге
# инструмента, черновик, README чужого репозитория внутри дерева (клон, снапшот, установленное
# окружение, сторонняя зависимость). Такой файл принадлежит тому, кто его завёл, и владения не
# открывает. Регистр расширения зоны не меняет: `docs/*.MD` — та же документация проекта.
DOC_ROOTS = ('docs', '.claude')
DOC_PATHS = project_conf.words("DOC_PATHS")
PLANS_DIR = project_conf.value("PLANS_DIR").strip("/")

# Проза владельцев — копия bash-части (её шапка): печатается лишь тогда, когда проверка не состоялась.
OWNERS_BRIEF = ("`.claude/agents|hooks|workflows` правит главная сессия, `.claude/skills`, канон "
                "`CLAUDE.md`, markdown корня проекта, markdown `.claude/**` вне зон выше, "
                "`docs/`" + "".join(", `%s`" % g for g in DOC_PATHS) + " — shared-skill-editor, "
                "`.claude/settings.json` — главная сессия, маркер "
                "`.claude/agents/<имя>.last` — агент этого имени и главная сессия, маркер "
                "`.claude/workflows/*.last` — главная сессия; "
                + ("`%s/` и " % PLANS_DIR if PLANS_DIR else "") +
                "markdown вне перечисленных мест под защиту не подпадают. Корневой `CLAUDE.md` "
                "внешнего контура — канон внешнего контура — правит shared-skill-editor; настройки внешнего "
                "контура — главная сессия; ведомая копия свода либо скрипта в контуре не правится "
                "никем: правка идёт в источник.")


def out(payload):
    print(json.dumps({"hookSpecificOutput": dict(hookEventName="PreToolUse", **payload)},
                     ensure_ascii=False))
    sys.exit(0)


def deny(reason):
    """Отказ обоими каналами разом: JSON permissionDecision и код 2 со stderr — skill
    `shared-gate-mechanics`."""
    print(json.dumps({"hookSpecificOutput": {"hookEventName": "PreToolUse",
                                             "permissionDecision": "deny",
                                             "permissionDecisionReason": reason}},
                     ensure_ascii=False))
    sys.stderr.write("[skill-guard] " + reason + "\n")
    sys.exit(2)


def under(rel, prefix):
    return rel == prefix or rel.startswith(prefix + '/')


def mask_hit(rel, mask):
    """Путь от корня под маской: сегмент к сегменту, `**` — любое число сегментов; регистр не
    различается, как и у расширения зоны."""
    parts, pats = rel.lower().split('/'), mask.lower().strip('/').split('/')

    def walk(i, j):
        if j == len(pats):
            return i == len(parts)
        if pats[j] == '**':
            return any(walk(k, j + 1) for k in range(i, len(parts) + 1))
        return i < len(parts) and fnmatch.fnmatchcase(parts[i], pats[j]) and walk(i + 1, j + 1)
    return walk(0, 0)


def is_doc(rel):
    """Markdown, принадлежащий владельцу сводов."""
    if not rel.lower().endswith('.md'):
        return False
    parts = rel.split('/')
    if len(parts) == 1:
        return True                  # корень проекта: CLAUDE.md, README.md и т.п.
    if parts[0] in DOC_ROOTS:
        return True
    return any(mask_hit(rel, m) for m in DOC_PATHS)


def contour_of(path):
    """Корень ВНЕШНЕГО контура, которому принадлежит путь. None — путь вне их."""
    for root in CONTOURS:
        if path == root or path.startswith(root + '/'):
            return root
    return None


def source_of(path):
    """Источник ведомой копии. Раскладка зеркала пути не меняет: копия лежит у адресата по тому же
    относительному адресу, что источник в проекте-источнике."""
    root = contour_of(path)
    return os.path.join(PROJ, path[len(root) + 1:]) if root else path


def owners_of(path):
    """Список agent_type, которым путь разрешён. None — путь не защищён.
    Порядок ветвей значим: `.claude/agents/<имя>.md` принадлежит главной сессии, хотя и оканчивается `.md`."""
    if '/vendor/' in path or '/node_modules/' in path:
        return None
    root = contour_of(path)
    if root is not None:
        rel = path[len(root) + 1:]
        if rel == 'CLAUDE.md':
            return ['shared-skill-editor']  # канон контура: своего источника в проекте-источнике у него нет
        if rel == '.claude/settings.json':
            # Реестр хуков КОНТУРА: копия этого гейта работает там только зарегистрированной.
            # Владельцы те же, что у здешнего реестра, — роль файла та же, а своей таблицы владения
            # сторона не ведёт.
            return ['']
        # Ведомая копия опознаётся ПОМЕТКОЙ, не местом: каталоги сводов и хуков у адресата общие с
        # его собственными файлами, и совпадение имени с носителем источника ничего не значит.
        if under(rel, '.claude') and marked(path):
            return [MIRROR]
        return None              # прочее содержимое внешнего репозитория ведёт своя сторона
    if not PROJ or not path.startswith(PROJ + '/'):
        return None
    rel = path[len(PROJ) + 1:]
    if PLANS_DIR and under(rel, PLANS_DIR):
        return None                  # план-файлы ведёт главная сессия; ветка — до markdown `.claude/**`
    if rel.endswith('.last'):
        if under(rel, '.claude/workflows'):
            # Маркер зоны аудита ведёт главная сессия: пустой agent_type тут ВЛАДЕЛЕЦ, не отсутствие
            # проверки. Отметка держит границу накопления зоны — сдвинутая не тем, кто вёл проход,
            # она молча обнуляет накопленное, и отказа при этом не приходит ниоткуда.
            return ['']
        if under(rel, '.claude/agents'):
            return [os.path.basename(rel)[:-5], '']
    if rel == '.claude/settings.json':
        # Регистрация хуков лежит здесь: правкой этого файла снимается сама проверка владения,
        # и без ветви гейт обходится одним вызовом — в том числе субагентом, которому он и адресован.
        # Пустая строка в списке — главная сессия (agent_type у неё пуст): настройка harness её работа.
        return ['']
    if under(rel, '.claude/agents') or under(rel, '.claude/hooks') or under(rel, '.claude/workflows'):
        return ['']                  # агенты, хуки и чек-листы аудита ведёт главная сессия
    if under(rel, '.claude/skills') or is_doc(rel):
        return ['shared-skill-editor']      # своды, CLAUDE.md, документация проекта
    return None


def foreign(paths, agent):
    """Пары «путь — владельцы» для тех путей, что текущему исполнителю не принадлежат."""
    res = []
    for path in paths:
        own = owners_of(path)
        if own is not None and agent not in own:
            res.append((path, own))
    return res


def relaxable(path, own):
    """Путь под послаблением главной сессии на точечную правку: своды, документация, агенты, хуки,
    чек-листы аудита, канон контура — всё, что ведёт агент-владелец. Маркер `.last` (владение своё, по
    имени носителя) и ведомая копия (владельца нет) — вне послабления; шапка хука."""
    return own != [MIRROR] and not path.endswith('.last')


def point_edit_miss(tool_name, tool_input, path=""):
    """None — вызов есть точечная правка; иначе — чем он ею не является, словами для отказа.
    Поле, которого у вызова нет, читается пустым: отказ, не пропуск."""
    if tool_name == "Write":
        content = tool_input.get("content")
        if not re.search(r'/\.claude/skills/[^/]+/SKILL\.md$', path):
            return "Write не нового свода"
        if os.path.exists(path):
            return "Write существующего файла"
        if not isinstance(content, str) or len(content.splitlines()) > POINT_EDIT_LINES:
            return "Write нового свода длиннее %d строк" % POINT_EDIT_LINES
        return None
    if tool_name != "Edit":
        return tool_name
    old, new = tool_input.get("old_string"), tool_input.get("new_string")
    if not isinstance(old, str) or not old:
        return "Edit с пустым old_string"
    if not isinstance(new, str):
        return "Edit без new_string"
    n = len(new.splitlines())
    if n > POINT_EDIT_LINES:
        return "Edit, new_string %d строк" % n
    return None


def shell_hint(pairs, agent):
    """Хвост о послаблении для главной сессии в ветке Bash: хотя бы один отбитый путь под ним."""
    if agent or not any(relaxable(p, o) for p, o in pairs):
        return None
    return "команда оболочки"


def addressee(own):
    """Владельцы пути словами. Главную сессию субагентом не адресуешь: agent_type у неё пуст,
    и `Agent(subagent_type=)` назвал бы отказу несуществующего исполнителя."""
    return ' либо '.join('из главной сессии' if o == '' else 'через Agent(subagent_type=%s)' % o
                         for o in own)


def refuse(pairs, agent, channel, point=None):
    """point — чем вызов главной сессии по пути под послаблением не является точечной правкой;
    None — хвост о послаблении не печатается (субагент, путь вне послабления)."""
    seen, parts = [], []
    for path, own in pairs:
        if path in seen:
            continue
        seen.append(path)
        if own == [MIRROR]:
            parts.append("%s — ведомая копия, правка в неё теряется на следующей сверке зеркала: "
                         "вносить в источник %s" % (path, source_of(path)))
        else:
            parts.append("%s — правки только %s" % (path, addressee(own)))
        if len(parts) == 4:
            break
    who = "агент %s" % agent if agent else "главная сессия"
    tail = ("" if channel == "file" else
            " Команда оболочки владение путём не обходит: проверка та же, что у файлового тула.")
    if point is not None:
        tail += (" Исключение главной сессии — точечная правка: Edit с непустым old_string и "
                 "new_string до %d строк включительно либо Write нового свода до %d строк "
                 "включительно; серию точечных правок закрывает проверяющий проход владельца. "
                 "Этот вызов — %s." % (POINT_EDIT_LINES, POINT_EDIT_LINES, point))
    deny("Отклонено правилом проекта (shared-knowledge-place «Правки конфигурации проекта»): "
         + ' | '.join(parts) + ". Текущий исполнитель — " + who + "." + tail)


raw = os.environ.get("HOOK_INPUT", "")
try:
    data = json.loads(raw)
except Exception as e:
    if re.search(r'"tool_name"\s*:\s*"Bash"', raw):
        out({"additionalContext": "skill-guard: вход хука не разобран (%s) — владение путём не "
                                  "проверено. Вызов не блокирован. Проверь сам: %s" % (e, OWNERS_BRIEF)})
    deny("вход хука не разобран (%s) — проверить владельца защищённого пути нечем" % e)

agent = data.get("agent_type") or ""
cwd = data.get("cwd") or PROJ or os.getcwd()
tool_input = data.get("tool_input") or {}

if (data.get("tool_name") or "") == "Bash":
    found, unresolved, eff_cwd, command = scan_command(tool_input.get("command") or "", cwd)
    bad = foreign([p for p, _ in found], agent)
    if bad:
        refuse(bad, agent, "bash", shell_hint(bad, agent))
    if unresolved:
        bad = foreign(sweep(command, eff_cwd, unresolved), agent)
        if bad:
            refuse(bad, agent, "bash", shell_hint(bad, agent))
    sys.exit(0)

file_path = tool_input.get("file_path") or ""
if not file_path:
    sys.exit(0)                      # у части перехваченных тулов путь лежит в своём поле
path = normalize(file_path, cwd)
own = owners_of(path)
if own is None or agent in own:
    sys.exit(0)
point = None
if not agent and relaxable(path, own):
    point = point_edit_miss(data.get("tool_name") or "", tool_input, path)
    if point is None:
        sys.exit(0)                  # точечная правка главной сессии — послабление (шапка хука)
refuse([(path, own)], agent, "file", point)
PY
rc=$?
case "$rc" in 0|2) exit "$rc";; esac
unchecked "разбор упал с кодом $rc"
