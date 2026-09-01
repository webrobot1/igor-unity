#!/bin/bash

# Ведомая копия. Источник — /var/www/html/game/.claude/hooks/skill-guard.sh в серверном
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
# Сам разбор команды — общий носитель lib/write_targets.py: какие формы записи он ловит и что
# проходит мимо, объявлено там. Здесь живёт только таблица владения путями.
# КОНТУРОВ, чей канон защищается, три: серверный проект и два ВНЕШНИХ репозитория — клиент Unity и
# игровой узел. У каждого внешнего свой корневой `CLAUDE.md`, и он не ведомая копия серверного, а
# самостоятельный носитель: источника в серверном репозитории у него нет, правка идёт по месту.
# Рядом с ним в тех же репозиториях лежат ВЕДОМЫЕ копии сводов и скриптов, и правило у них обратное —
# правка копии теряется на следующей сверке зеркала, вносить её надо в серверный источник. Различает
# их ПОМЕТКА ведомости, общий носитель `lib/mirror_copy.py`: имя и каталог тут не признак — каталоги
# у адресата общие с его собственными файлами, а имя копии совпадает с именем источника. Прочее
# содержимое внешних репозиториев не защищено вовсе: их README и код ведёт своя сторона. Реестр
# настроек контура под защитой наравне со здешним: им запускается копия ЭТОГО гейта у той стороны,
# и снятая регистрация гасит её молча — ни отказа, ни следа ни в одной из двух сессий.
# Корни контуров стоят литералом ниже, как у соседних хуков проекта (`audit-backlog.sh`,
# `client-skills-mirror.sh`), и набор самопроверки подменяет их песочницей. Корень СЕРВЕРНОГО
# проекта стоит литералом там же и по той же причине, что и они: хук зеркалируется в оба внешних
# контура и работает там КОПИЕЙ, зарегистрированной в их собственных настройках, — своё
# расположение у копии лежит в чужом репозитории, и вычисленный из него корень назвал бы серверным
# проектом сам этот репозиторий. Тогда ветвь серверных путей стала бы недостижимой (корень совпал
# бы с корнем контура, а тот проверяется раньше), а отказ на правке ведомой копии адресовал бы за
# источником её саму. Каталог `lib` остаётся от СВОЕГО расположения: модули копия импортирует свои.
#
# Маркеры `.last` протокола fileagent — своё владение: носитель ведёт маркер СВОЕГО имени, чужой
# маркер отбивается ему наравне с прочими защищёнными путями. Пишется маркер командой оболочки
# (значение его есть вывод git-команды — skill `fileagent`), потому владение по нему проверяет
# ветка Bash. Маркер конвейера ведёт главная сессия: пустой agent_type тут ВЛАДЕЛЕЦ, а не
# отсутствие проверки, и субагенту такой путь отбивается наравне с прочими.

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

proj="/var/www/html/game"
hooks="$(cd "$(dirname "$0")" 2>/dev/null && pwd)"
client_root="/mnt/c/Unity/release"
node_root="/var/www/html/node"

if ! command -v python3 >/dev/null 2>&1; then
  if printf '%s' "$input" | grep -q '"tool_name"[[:space:]]*:[[:space:]]*"Bash"'; then
    echo '{"hookSpecificOutput":{"hookEventName":"PreToolUse","additionalContext":"skill-guard: владение путём не проверено — в окружении нет python3. Вызов не блокирован. Проверь сам: `.claude/agents|hooks|workflows` правит team-lead, `.claude/skills`, markdown корня проекта (`CLAUDE.md`, `README.md`), markdown `.claude/**` вне зон выше, `docs/`, `.install/`, `Build/*.md` и `Build/<бандл>/instructions.md` — skill-editor, `.claude/settings.json` — team-lead и главная сессия, маркер `.claude/agents/<имя>.last` — агент этого имени и team-lead, маркер `.claude/workflows/*.last` — главная сессия; `Plans/` и markdown вне перечисленных мест под защиту не подпадают. Корневой `CLAUDE.md` внешнего контура — канон клиентского репозитория и канон репозитория узла — правит skill-editor; настройки внешнего контура — team-lead и главная сессия; ведомая копия свода либо скрипта в этих контурах не правится никем: правка идёт в серверный источник. Командой оболочки эта проверка не обходится."}}'
    exit 0
  fi
  echo "[skill-guard] в окружении нет python3 — проверить владельца защищённого пути нечем: правка отклонена" >&2
  exit 2
fi

HOOK_INPUT="$input" PROJ="$proj" HOOKS_DIR="$hooks" CLIENT_ROOT="$client_root" NODE_ROOT="$node_root" python3 - <<'PY'
import json, os, re, sys

sys.path.insert(0, os.path.join(os.environ.get("HOOKS_DIR") or "", "lib"))
from write_targets import normalize, scan_command, sweep
from mirror_copy import marked

PROJ = (os.environ.get("PROJ") or "").rstrip("/")

# Корни ВНЕШНИХ контуров: репозиторий клиента Unity и репозиторий игрового узла. Недоступный корень
# (диск клиента не смонтирован, репозитория узла на машине нет) ветви не меняет — вердикт по пути
# считается по самому пути, а пометка ведомости у нечитаемого файла не находится и путь остаётся
# незащищённым: писать по такому пути всё равно нечем.
CONTOURS = tuple(r.rstrip("/") for r in (os.environ.get("CLIENT_ROOT") or "",
                                         os.environ.get("NODE_ROOT") or "") if r)

# Псевдо-владелец ведомой копии: реального владельца у неё нет — правка теряется у КАЖДОГО
# исполнителя, включая владельца сводов, и адресуется она не агенту, а серверному источнику.
MIRROR = "\x00mirror"


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
    sys.stderr.write("[skill-guard] " + reason + "\n")
    sys.exit(2)


def under(rel, prefix):
    return rel == prefix or rel.startswith(prefix + '/')


# Перечень владельцев прозой — для fail-open ветви: вызов там не блокируется, а свод правил
# («Правки конфигурации проекта») у адресата инжекта может не быть загружен ни одним каналом.
# Копий прозы ДВЕ — вторая в bash-ветви выше, — и печатаются обе лишь тогда, когда проверка
# не состоялась: разъезд их не виден ни на одном рабочем вызове, оттого его ловит набор
# самопроверки. Сама проза с таблицей тоже сверяется набором: кейс «проза зоны владения ↔
# фактический вердикт гейта» разбирает её на классы путей и спрашивает вердикт по каждому.
OWNERS_BRIEF = ("`.claude/agents|hooks|workflows` правит team-lead, `.claude/skills`, markdown "
                "корня проекта (`CLAUDE.md`, `README.md`), markdown `.claude/**` вне зон выше, "
                "`docs/`, `.install/`, `Build/*.md` и `Build/<бандл>/instructions.md` — skill-editor, "
                "`.claude/settings.json` — team-lead и главная сессия, маркер "
                "`.claude/agents/<имя>.last` — агент этого имени и team-lead, маркер "
                "`.claude/workflows/*.last` — главная сессия; `Plans/` и markdown вне "
                "перечисленных мест под защиту не подпадают. Корневой `CLAUDE.md` внешнего "
                "контура — канон клиентского репозитория и канон репозитория узла — правит "
                "skill-editor; настройки внешнего контура — team-lead и главная сессия; ведомая "
                "копия свода либо скрипта в этих контурах не правится никем: правка идёт в "
                "серверный источник.")


# Зона владельца сводов — НОСИТЕЛИ, названные core «Правки конфигурации проекта»: своды, `CLAUDE.md`,
# документация проекта, внешние MCP-инструкции бандла. Расширением имени она не задаётся: markdown
# лежит и вне её — артефакт прогона в каталоге инструмента, черновик, README чужого репозитория
# внутри дерева (git-baseline `storage/`, снапшот чужой игры, установленное окружение). Такой файл
# принадлежит тому, кто его завёл, и владения не открывает. Регистр расширения зоны не меняет:
# `docs/*.MD` — та же документация проекта.
DOC_ROOTS = ('docs', '.install', '.claude')


def is_doc(rel):
    """Markdown, принадлежащий владельцу сводов."""
    if not rel.lower().endswith('.md'):
        return False
    parts = rel.split('/')
    if len(parts) == 1:
        return True                  # корень проекта: CLAUDE.md, README.md, CREDENTIALS.md
    if parts[0] in DOC_ROOTS:
        return True
    if parts[0] == 'Build':
        # `Build/README.md` — раскладка бандлов; `Build/<бандл>/instructions.md` — инструкции
        # MCP-сервера бандла внешнему клиенту. Глубже лежат README сторонних библиотек бандла.
        return len(parts) == 2 or (len(parts) == 3 and parts[2] == 'instructions.md')
    return False


def contour_of(path):
    """Корень ВНЕШНЕГО контура, которому принадлежит путь. None — путь вне их."""
    for root in CONTOURS:
        if path == root or path.startswith(root + '/'):
            return root
    return None


def source_of(path):
    """Серверный источник ведомой копии. Раскладка зеркала пути не меняет: копия лежит у адресата
    по тому же относительному адресу, что источник в серверном проекте."""
    root = contour_of(path)
    return os.path.join(PROJ, path[len(root) + 1:]) if root else path


def owners_of(path):
    """Список agent_type, которым путь разрешён. None — путь не защищён.
    Порядок ветвей значим: `.claude/agents/qa-server.md` принадлежит team-lead, хотя и оканчивается `.md`."""
    if '/vendor/' in path or '/node_modules/' in path:
        return None
    root = contour_of(path)
    if root is not None:
        rel = path[len(root) + 1:]
        if rel == 'CLAUDE.md':
            return ['skill-editor']  # канон контура: своего источника в серверном проекте у него нет
        if rel == '.claude/settings.json':
            # Реестр хуков КОНТУРА: копия этого гейта работает там только зарегистрированной.
            # Владельцы те же, что у здешнего реестра, — роль файла та же, а своей таблицы владения
            # сторона не ведёт.
            return ['team-lead', '']
        # Ведомая копия опознаётся ПОМЕТКОЙ, не местом: каталоги сводов и хуков у адресата общие с
        # его собственными файлами, и совпадение имени с серверным носителем ничего не значит —
        # авто-генерённые доки тулов лежат под теми же именами в обоих контурах.
        if under(rel, '.claude') and marked(path):
            return [MIRROR]
        return None              # прочее содержимое внешнего репозитория ведёт своя сторона
    if not PROJ or not path.startswith(PROJ + '/'):
        return None
    rel = path[len(PROJ) + 1:]
    if under(rel, 'Plans'):
        return None                  # план-файлы ведёт главная сессия
    if rel.endswith('.last'):
        if under(rel, '.claude/workflows'):
            # Маркер конвейера ведёт главная сессия: пустой agent_type тут ВЛАДЕЛЕЦ, не отсутствие
            # проверки. Отметка держит границу накопления зоны — сдвинутая не тем, кто вёл проход,
            # она молча обнуляет накопленное, и отказа при этом не приходит ниоткуда.
            return ['']
        if under(rel, '.claude/agents'):
            return [os.path.basename(rel)[:-5], 'team-lead']
    if rel == '.claude/settings.json':
        # Регистрация хуков лежит здесь: правкой этого файла снимается сама проверка владения,
        # и без ветви гейт обходится одним вызовом — в том числе субагентом, которому он и адресован.
        # Пустая строка в списке — главная сессия (agent_type у неё пуст): настройка harness её работа.
        return ['team-lead', '']
    if under(rel, '.claude/agents') or under(rel, '.claude/hooks') or under(rel, '.claude/workflows'):
        return ['team-lead']
    if under(rel, '.claude/skills') or is_doc(rel):
        return ['skill-editor']      # своды, CLAUDE.md, документация проекта, instructions бандла
    return None


def foreign(paths, agent):
    """Пары «путь — владельцы» для тех путей, что текущему исполнителю не принадлежат."""
    res = []
    for path in paths:
        own = owners_of(path)
        if own is not None and agent not in own:
            res.append((path, own))
    return res


def addressee(own):
    """Владельцы пути словами. Главную сессию субагентом не адресуешь: agent_type у неё пуст,
    и `Agent(subagent_type=)` назвал бы отказу несуществующего исполнителя."""
    return ' либо '.join('из главной сессии' if o == '' else 'через Agent(subagent_type=%s)' % o
                         for o in own)


def refuse(pairs, agent, channel):
    seen, parts = [], []
    for path, own in pairs:
        if path in seen:
            continue
        seen.append(path)
        if own == [MIRROR]:
            parts.append("%s — ведомая копия, правка в неё теряется на следующей сверке зеркала: "
                         "вносить в серверный источник %s" % (path, source_of(path)))
        else:
            parts.append("%s — правки только %s" % (path, addressee(own)))
        if len(parts) == 4:
            break
    who = "агент %s" % agent if agent else "главная сессия"
    tail = ("" if channel == "file" else
            " Команда оболочки владение путём не обходит: проверка та же, что у файлового тула.")
    deny("Отклонено правилом проекта (core «Правки конфигурации проекта»): "
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
        refuse(bad, agent, "bash")
    if unresolved:
        bad = foreign(sweep(command, eff_cwd, unresolved), agent)
        if bad:
            refuse(bad, agent, "bash")
    sys.exit(0)

file_path = tool_input.get("file_path") or ""
if not file_path:
    sys.exit(0)                      # у части перехваченных тулов путь лежит в своём поле
own = owners_of(normalize(file_path, cwd))
if own is None or agent in own:
    sys.exit(0)
refuse([(normalize(file_path, cwd), own)], agent, "file")
PY
