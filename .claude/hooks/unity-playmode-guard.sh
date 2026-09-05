#!/bin/bash
# PreToolUse hook (mcp__ai-game-developer__editor-application-set-state|Bash): запуск Play Mode при
# несохранённой сцене отклоняется. Модальное окно «Scene(s) Have Been Modified» блокирует главный
# поток редактора, а MCP-плагин исполняет в нём все вызовы — после появления окна не проходит ни
# один тул, включая закрывающий его; снимает только человек кнопкой в редакторе. Дисциплинарное
# правило клиентского канона от этого не удержало — гейт механический.
# Срабатывает только на isPlaying=true: остановка Play Mode и пауза окна не дают, сети не касаются.
# Каналов входа ДВА, и гейт накрывает оба (skill `gate-mechanics`, условие накрывает КЛАСС форм
# вызова): тул MCP-сервера — у сессии, которой сервер подключён, — и команда оболочки
# `npx unity-mcp-cli run-tool editor-application-set-state` — у сессии, которой тулы этого сервера
# не поданы вовсе. Исход у обоих один, а условие на одно имя тула второй канал не видит.
# Команду опознаёт по ПОЗИЦИИ имени в части цепочки общий носитель lib/unity_cli.py (поверх
# lib/chain_parser.py), не по вхождению текста: тем же текстом вызов ходит аргументом чужой
# команды — телом heredoc, шаблоном поиска, фикстурой набора самопроверки. Носитель общий с
# unity-playmode-stop-reminder.sh: тот стоит на ТОМ ЖЕ вызове своим событием, и вторая копия
# разбора разошлась бы молча. В клиентский контур модуль доезжает зеркалированием как зависимость
# того хука — связь косвенная, оттого её обрыв гейт называет пометкой, а не падением.
# Флаг запуска у канала CLI лежит в JSON параметра `--input`, адрес редактора — в `--url` самой
# команды: имени MCP-сервера в ней нет и `.mcp.json` тут ни при чём.
# Матчер настроек ловит ВСЕ команды оболочки — оттого перед запуском интерпретатора стоит дешёвый
# отсев по имени тула: событие, его не называющее, гейта не касается ни одним каналом.
# Третьей формы у класса нет: сырое JSON-RPC по порту плагина (`tools/call` через curl либо скрипт)
# каналом агента не является вовсе — запрет держит свод `unity`, и гейт его не дублирует.
# Состояние спрашивается у самого Unity (scene-list-opened → IsDirty); адрес сервера читается из
# .mcp.json ПРОЕКТА СЕССИИ по имени, взятому из tool_name: файл один на оба проекта (серверный и
# клиентский), а реестр адресов у каждого свой — переезд порта правится в реестре своего проекта.
# Регистрируется в обоих проектах абсолютным путём и запускается через `bash`: на диске Windows
# бит исполнения не держится, маунт его сбрасывает.
# Уже идущий Play Mode — ОТКАЗ: вход в идущую игру канон клиента запрещает. Кто её запустил, ни
# одним каналом не наблюдаемо — `editor-application-get-state` отдаёт сам факт игры, без автора, —
# оттого хук называет наблюдаемое и адресует канон, а случаи «своя» и «не своя» разводит он сам:
# копия критерия в хуке разошлась бы с ним молча. Ветвь несохранённых сцен за отказом об идущей
# игре недостижима: IsDirty в Play Mode относится к временной копии сцены и о редакторской не
# говорит ничего.
# Момент ЗАВЕРШЕНИЯ ответа при идущей игре держит unity-playmode-stop-reminder.sh: он спрашивает
# то же состояние у того же сервера, но на своём событии и без блокировки.
# Unity не ответил (Editor выключен, плагин не поднят, таймаут) — вызов НЕ блокируется: инжектится
# строка о непройденной проверке, иначе хук ронял бы работу при просто выключенном редакторе.

input=$(cat)

# Отсев до запуска интерпретатора: имя тула стоит в событии обоих каналов — именем вызываемого тула
# у MCP, позиционным аргументом команды у CLI. Не названо — разбирать нечего.
case "$input" in
  *editor-application-set-state*) ;;
  *) exit 0 ;;
esac

if ! command -v python3 >/dev/null 2>&1; then
  # Разобрать вход нечем: сузить до запуска Play Mode грубым признаком, чтобы не шуметь на остановке.
  case "$input" in
    *isPlaying*[Tt]rue*)
      echo '{"hookSpecificOutput":{"hookEventName":"PreToolUse","additionalContext":"unity-playmode-guard: проверка состояния сцен не выполнена — в окружении нет python3. Запуск Play Mode не блокирован. Проверь IsDirty сам (scene-list-opened): вход в Play Mode при несохранённой сцене даёт модальное окно, снимаемое только человеком."}}'
      ;;
  esac
  exit 0
fi

hooks="$(cd "$(dirname "$0")" 2>/dev/null && pwd)"
self="$(cd "$(dirname "$0")/../.." 2>/dev/null && pwd)"

HOOK_INPUT="$input" python3 - "${CLAUDE_PROJECT_DIR:-}" "$self" "$hooks" <<'PY'
import json, os, re, sys, time, urllib.request

PROJECT_DIR, SELF_ROOT, HOOKS_DIR = sys.argv[1], sys.argv[2], sys.argv[3]
DEADLINE = time.monotonic() + 7.0
STATE = "editor-application-set-state"
# Явная ОСТАНОВКА в тексте параметра `--input`, когда JSON из него не разобран: значение приходит
# переменной оболочки либо теряет кавычки на разборе команды. Разделитель между именем и значением
# бывает любым (`":`, `: `, `=`) — оттого он и не перечисляется.
STOP_FLAG = re.compile(r"isPlaying\D{0,4}(false|0|no)\b", re.I)
OPENER = urllib.request.build_opener(urllib.request.ProxyHandler({}))

MODAL = ("Вход в Play Mode при несохранённой сцене даёт модальное окно «Scene(s) Have Been "
         "Modified». Оно блокирует главный поток редактора, и дальше не проходит ни один вызов "
         "MCP, включая закрывающий само окно. Снять его может только человек кнопкой в редакторе.")


def context(msg):
    print(json.dumps({"hookSpecificOutput": {"hookEventName": "PreToolUse",
                                             "additionalContext": "unity-playmode-guard: " + msg}},
                     ensure_ascii=False))
    sys.exit(0)


def deny(reason):
    """Отказ обоими каналами разом: JSON permissionDecision и код 2 со stderr — skill
    `gate-mechanics`."""
    print(json.dumps({"hookSpecificOutput": {"hookEventName": "PreToolUse",
                                             "permissionDecision": "deny",
                                             "permissionDecisionReason": reason}},
                     ensure_ascii=False))
    sys.stderr.write("[unity-playmode-guard] " + reason + "\n")
    sys.exit(2)


def unchecked(reason):
    context("проверить состояние сцен не удалось (%s). Запуск Play Mode не блокирован. Проверь "
            "IsDirty сам (scene-list-opened). %s" % (reason, MODAL))


try:
    d = json.loads(os.environ.get("HOOK_INPUT", ""))
except Exception:
    sys.exit(0)


def truthy(value):
    """Флаг запуска: клиент вправе прислать его текстом."""
    if isinstance(value, str):
        return value.strip().lower() in ("true", "1", "yes")
    return bool(value)


def cli_launch(command):
    """Запуск Play Mode командой оболочки: (запуск ли, адрес редактора из `--url`).

    Флаг лежит в JSON параметра `--input`. JSON не разобран — значение читается по СЫРОМУ тексту
    параметра: он приходит переменной оболочки (`--input "$JSON"`) либо теряет кавычки на разборе
    команды, а исход у гейта разный. Явная остановка распознаётся и пропускается; всё прочее, в
    том числе отсутствие значения вовсе, считается ЗАПУСКОМ — цена промаха односторонняя, тихий
    пропуск вешает редактор модальным окном, а лишняя проверка стоит одного вызова к редактору и
    на чистых сценах не держит никого.
    """
    try:
        sys.path.insert(0, os.path.join(HOOKS_DIR, "lib"))
        from unity_cli import tool_calls
    except Exception as e:
        unchecked("общий разбор команды не загрузился (%s): модуль lib/unity_cli.py рядом с хуком "
                  "не лежит — в этот контур он доезжает зеркалированием серверного репозитория" % e)

    launch, address = False, ""
    for opts in tool_calls(command, STATE):
        raw = opts.get("--input") or ""
        try:
            parsed = json.loads(raw)
        except ValueError:
            parsed = None

        if isinstance(parsed, dict):
            starts = truthy(parsed.get("isPlaying", True))
        else:
            starts = not STOP_FLAG.search(raw)

        if starts:
            launch = True
            address = opts.get("--url") or address

    return launch, address


tool = d.get("tool_name") or ""
given = d.get("tool_input") or {}

if tool == "Bash":
    launch, url = cli_launch(given.get("command") or "")
    if not launch:
        sys.exit(0)
    if not url:
        unchecked("в команде нет --url, а имени MCP-сервера канал CLI не несёт — адрес редактора "
                  "взять неоткуда")
else:
    if not truthy(given.get("isPlaying")):
        sys.exit(0)

    # Имя сервера берётся у сработавшего тула, а не литералом: у хука и матчера один источник.
    parts = tool.split("__")
    server = parts[1] if len(parts) > 2 and parts[0] == "mcp" else ""
    if not server:
        unchecked("в событии нет имени MCP-сервера")

    url = ""
    for base in (PROJECT_DIR, d.get("cwd") or "", SELF_ROOT):
        if not base:
            continue
        try:
            with open(os.path.join(base, ".mcp.json"), encoding="utf-8") as f:
                url = json.load(f)["mcpServers"][server]["url"]
            break
        except Exception:
            continue
    if not url:
        unchecked("адрес сервера %s не прочитан из .mcp.json" % server)


def rpc(payload, sid=None):
    req = urllib.request.Request(url, data=json.dumps(payload).encode("utf-8"), method="POST")
    req.add_header("Content-Type", "application/json")
    req.add_header("Accept", "application/json, text/event-stream")
    if sid:
        req.add_header("Mcp-Session-Id", sid)
    left = DEADLINE - time.monotonic()
    if left <= 0:
        raise TimeoutError("исчерпан бюджет ожидания")
    with OPENER.open(req, timeout=min(3.0, left)) as r:
        return r.headers, r.read().decode("utf-8", "replace")


def result(text):
    """Тело HTTP-транспорта приходит SSE-строкой `data: {...}`, stdio-транспорта — голым JSON."""
    for line in text.splitlines():
        if line.startswith("data:"):
            try:
                return json.loads(line[5:].strip())["result"]["structuredContent"]["result"]
            except Exception:
                pass
    return json.loads(text)["result"]["structuredContent"]["result"]


def call(name, sid):
    return result(rpc({"jsonrpc": "2.0", "id": 2, "method": "tools/call",
                       "params": {"name": name, "arguments": {}}}, sid)[1])


try:
    headers, _ = rpc({"jsonrpc": "2.0", "id": 1, "method": "initialize",
                      "params": {"protocolVersion": "2024-11-05", "capabilities": {},
                                 "clientInfo": {"name": "unity-playmode-guard", "version": "1"}}})
    sid = headers.get("Mcp-Session-Id")
    if sid:
        rpc({"jsonrpc": "2.0", "method": "notifications/initialized"}, sid)
except Exception as e:
    unchecked("Unity MCP не ответил: %s" % e)

try:
    already_playing = bool(call("editor-application-get-state", sid).get("IsPlaying"))
except Exception:
    already_playing = False  # состояние неизвестно — режим правки тут обычный случай
if already_playing:
    deny("Запуск Play Mode отклонён: редактор отвечает, что игра УЖЕ ИДЁТ "
         "(editor-application-get-state → IsPlaying: true). Кто её запустил, состояние редактора "
         "не называет. Что с этим делать — канон клиента /mnt/c/Unity/release/CLAUDE.md, «Вход в "
         "игру».")

try:
    scenes = call("scene-list-opened", sid)
    dirty = [s.get("Name") or s.get("path") or "?" for s in scenes if s.get("IsDirty")]
except Exception as e:
    unchecked("Unity MCP не ответил: %s" % e)

if not dirty:
    sys.exit(0)

# Текст отказа — ОСОЗНАННЫЙ дубль клиентского CLAUDE.md, «Вход в игру»: тот лежит в другом
# репозитории и в контекст серверной сессии сам не попадает — агенту предписано прочитать его
# файлом, доставка вероятностна. Точка необратима: промах вешает редактор модальным окном,
# снять которое может только человек. Правится парно с CLAUDE.md клиента.
deny("Запуск Play Mode отклонён: несохранённые изменения в сценах — %s.\n"
     "%s\n"
     "Изменения твои → сохранить (scene-save) и повторить запуск.\n"
     "Изменения не твои (параллельная сессия либо человек) → Play Mode не запускать вовсе, сцену "
     "не сохранять и не выгружать, задачу отложить и вернуть КООРДИНАТОРУ стоп-сигнал с названным "
     "ресурсом — редактор занят чужой работой (`core` «Нужный ресурс недоступен»); окно "
     "пользователю даёт он, у исполнителя интерактива нет."
     % (", ".join(dirty), MODAL))
PY
