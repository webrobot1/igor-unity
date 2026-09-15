#!/bin/bash

# Ведомая копия. Источник — /var/www/html/game/.claude/hooks/unity-playmode-stop-reminder.sh в
# серверном репозитории; копию перезаписывает его хук при каждой правке источника. Правка копии
# теряется молча — вносить её в источник. Серверного репозитория нет под рукой → назвать нужную
# правку пользователю.

# Stop|SubagentStop hook: идущий Play Mode и открытый Unity Editor на завершении ответа. Момент один —
# ответ закончился, а игра идёт либо редактор открыт: до следующего сообщения они висят без
# присмотра, игра грузит машину, редактор держит гигабайты памяти. Игру, запущенную этим вызывающим
# и им не остановленную, хук ОСТАНАВЛИВАЕТ сам; чужую (после его собственной остановки) лишь
# называет. Редактор, в котором вызывающий работал, хук ЗАКРЫВАЕТ программой целиком, когда в нём
# нет игры, сборки, открытого префаба и несохранённой сцены; иначе называет причину. Порядок жизни
# редактора (поднимает и гасит тот, кто в нём работает) — свод `unity`; хук подпирает его моменты
# завершения ответа и вопроса человеку, конец пачки работы в редакторе остаётся за вызывающим.
# Шаговый вход один — ВОПРОС ЧЕЛОВЕКУ (`AskUserQuestion`): он равен концу ответа по СРОКУ — окно
# ждёт человека и не кончится, пока тот не ответит, хоть часами, — потому игра и редактор на нём
# гасятся, а не напоминаются. Напоминанием тут не обойтись вовсе: оно доедет до модели лишь после
# ответа человека, то есть после всего простоя, ради которого заведено. Прочих шаговых входов
# (правка файла, запуск агента, долгая команда) у хука нет: состояние берётся опросом живого
# редактора, и на каждой правке файла тот стоил бы сетевого вызова за правку; ярлык шага считает
# общий носитель lib/transcript.py (leaving_step) — его же делит напоминание об оставленном браузере.
# Момент ВХОДА в игру держит гейт клиентского репозитория unity-playmode-guard.sh: он стоит на
# самом вызове запуска и спрашивает то же состояние. Момент первого вызова тула редактора и
# момент запуска игры («остановить по сбору данных») держит unity-skill-reminder.sh.
#
# Состояние даёт САМ редактор (`editor-application-get-state`), не транскрипт: игру останавливает
# кто угодно мимо хука — человек в редакторе, параллельная сессия, — и счёт по транскрипту
# расходился бы с действительностью молча. Редактор не ответил (Editor выключен, плагин не поднят,
# таймаут) — хук молчит: закрывать и называть нечего.
# Канал опроса — прямой JSON-RPC по адресу MCP-сервера. `npx unity-mcp-cli run-tool` отдаёт то же
# состояние за 2.9 с против 0.25 с у полного вызова отсюда (замер 05.09.2026), а цена платится на
# КАЖДОМ завершении ответа Unity-сессии. Запрет свода `unity` на сырое рукопожатие — о канале
# работы агента, не о коде хуков, ходящих по тому же порту. Тело опроса повторяет
# unity-playmode-guard.sh осознанно: тот живёт в клиентском репозитории и уезжает вместе с ним, а
# общий модуль `lib/` доезжает туда лишь как зависимость ЗЕРКАЛИРУЕМОГО скрипта — связь косвенная.
# Разбор CLI-вызова оба берут вопреки ей общим носителем (`lib/unity_cli.py`): расхождение форм у
# двух каналов ОДНОГО вызова молчаливо, а обрыв самой связи гейт называет вслух пометкой.
#
# Закрытие — код в самом редакторе (`script-execute`): проверка того, что выход не потеряет, и
# прямой `EditorApplication.Exit(0)`. Отложенный выход (`EditorApplication.delayCall`) редактор без
# фокуса не исполняет — кадр у него не идёт. Выход обрывает соединение вызова, итог хук берёт
# опросом: редактор больше не отвечает. Несохранённые правки ассетов и раскладку окон редактор не
# показывает — выход их не спрашивает; закрывать и редактор, запущенный человеком, — решение
# пользователя (свод `unity`).
# Фоновый исполнитель вызывающего, чей конец в транскрипт ещё не пришёл, может работать в том же
# редакторе: закрытие на завершении ответа координатора оборвало бы его работу — редактор тогда не
# закрывается, а называется. Свой конец исполнитель закрывает сам — тем же хуком на SubagentStop.
#
# Автора запуска игры хук не называет: `editor-application-get-state` отдаёт сам факт игры, без
# автора. Текст сообщает наблюдаемое и отсылает к канону клиента (/mnt/c/Unity/release/CLAUDE.md,
# «Вход в игру»): случаи «своя» и «не своя» разводит он, копия критерия в хуке разошлась бы с ним
# молча. Канон не наследуется ни одной сессией и доезжает только явным чтением — оттого его адрес
# стоит в самом напоминании.
#
# ЦЕНУ опроса гейтит транскрипт ВЫЗЫВАЮЩЕГО (lib/transcript.py, caller_transcript): опрос идёт,
# только когда вызывающий сам работал с редактором — тулом MCP-сервера плагина (субагент, которому
# сервер подключён) либо командой `npx unity-mcp-cli run-tool` (главная сессия, у которой тулов
# этого сервера нет). Состояния признак не даёт и дать не может: он лишь повод спросить редактор.
# Остановка игры и напоминание о чужой игре требуют большего повода — смены состояния редактора:
# вызывающий игру не запускал — по канону она ему «не своя», трогать её нельзя, и напоминание было
# бы шумом. Тем же вызовом берётся АДРЕС опроса: у канала CLI — `--url` самой команды, у канала
# MCP — имя сервера из имени тула плюс `.mcp.json` проекта сессии (файл один на оба проекта, реестр
# адресов у каждого свой). Команда опознаётся по ПОЗИЦИИ имени в части цепочки (lib/unity_cli.py
# поверх lib/chain_parser.py), не по вхождению его в текст: тем же текстом вызов ходит аргументом
# чужой команды — телом heredoc, шаблоном поиска, фикстурой набора самопроверки. Носитель опознания
# общий с гейтом входа: тот стоит на ТОМ ЖЕ вызове, и вторая копия разбора разошлась бы с ним молча.
# `SubagentStop` без `agent_id` — молчание: свой эпизод адресовать нечем, а `transcript_path` ведёт
# тогда в чужой транскрипт.
# Маркера-троттлинга нет: состояние наблюдаемое, напоминание идёт, пока идёт игра либо открыт
# редактор, — закроют их хоть мимо хука, следующий опрос не ответит и хук смолкнет сам.
# Регистрируется в настройках ОБОИХ проектов абсолютным путём на Stop|SubagentStop: редактором
# пользуются и серверная сессия, и работающая из репозитория клиента, а настройки каждая читает свои.
# Не блокирует: инжектит additionalContext под `hookEventName` СВОЕГО события — поле из схемы
# соседнего программа отбрасывает молча. `decision: "block"` тут негоден: находка ШТАТНАЯ,
# состояния под чужое действие не несёт (skill `gate-mechanics`, «Канал ДОКЛАДА хука»).

input=$(cat)
hooks_dir="$(cd "$(dirname "$0")" && pwd)"

command -v python3 >/dev/null 2>&1 || exit 0

HOOK_INPUT="$input" python3 - "$hooks_dir" <<'PY' 2>/dev/null
import json, os, sys, time, urllib.request

sys.path.insert(0, os.path.join(sys.argv[1], "lib"))
from transcript import caller_transcript, leaving_step
from unity_cli import tool_calls

# Тул смены состояния редактора: MCP-канал зовёт его именем тула, CLI-канал — позиционным
# аргументом команды оболочки. Читающий тул даёт состояние, менять его не может — поводом к
# остановке игры не идёт, но работой в редакторе является.
STATE = "editor-application-set-state"
READ = "editor-application-get-state"
EXEC = "script-execute"
BUDGET = 3.0
# Выход компилирует скрипт в редакторе, а редактор без фокуса отвечает медленнее: бюджет свой.
CLOSE_BUDGET = 8.0

# Тулы плагина редактора по имени: у канала MCP сервер зовут как угодно, поэтому работу в редакторе
# отличает имя самого тула.
EDITOR_TOOLS = ("editor-application-", "script-", "scene-", "gameobject-", "assets-", "screenshot-",
                "console-", "object-", "reflection-", "profiler-", "package-", "tests-run",
                "type-get-json-schema", "unity-tool-list", "tool-set-enabled-state")

# Код выхода: сперва то, что выход потерял бы молча, затем сам выход. Строка ответа начинается с
# «не закрыт» — выход не шёл.
CLOSE_CODE = (
    "using UnityEditor; using UnityEditor.SceneManagement; using UnityEngine.SceneManagement; "
    "public class Script { public static string Main() { "
    "if (EditorApplication.isPlayingOrWillChangePlaymode) return \"не закрыт: идёт игра\"; "
    "if (EditorApplication.isCompiling || EditorApplication.isUpdating) return \"не закрыт: идёт сборка\"; "
    "if (PrefabStageUtility.GetCurrentPrefabStage() != null) return \"не закрыт: открыт префаб\"; "
    "for (int i = 0; i < SceneManager.sceneCount; i++) if (SceneManager.GetSceneAt(i).isDirty) "
    "return \"не закрыт: несохранённая сцена \" + SceneManager.GetSceneAt(i).name; "
    "EditorApplication.Exit(0); return \"выход\"; } }")

OPENER = urllib.request.build_opener(urllib.request.ProxyHandler({}))


def truthy(value):
    if isinstance(value, str):
        return value.strip().lower() in ("true", "1", "yes")
    return bool(value)


def is_editor_tool(tool):
    return tool.startswith(EDITOR_TOOLS)


def cli_state(command):
    """Последний вызов смены состояния через CLI в команде: (был ли, адрес --url, запуск ли).
    Флаг лежит в JSON параметра --input; не разобран — считается запуском, как у гейта входа."""
    seen, found, launch = False, "", False
    for opts in tool_calls(command, STATE):
        seen = True
        found = opts.get("--url") or found
        raw = opts.get("--input") or ""
        try:
            parsed = json.loads(raw)
        except ValueError:
            parsed = None
        launch = truthy(parsed.get("isPlaying", True)) if isinstance(parsed, dict) else "false" not in raw.lower()

    return seen, found, launch


def cli_any(command):
    """Адрес --url последнего вызова любого тула редактора через CLI в команде; пусто — не было."""
    found = ""
    for opts in tool_calls(command, None):
        found = opts.get("--url") or found
    return found


def scan(path):
    """Повод и адрес опроса по транскрипту вызывающего: (имя MCP-сервера, адрес из команды CLI,
    менял ли вызывающий состояние редактора, был ли последний такой вызов ЗАПУСКОМ игры, живые
    фоновые исполнители). Адрес берётся у ПОСЛЕДНЕЙ работы в редакторе; работы не было — первые две
    части пусты, опрос не идёт. Признак запуска решает исход игры: запущенную этим вызывающим и не
    остановленную им хук останавливает сам; игру после его же остановки запустил кто-то другой —
    о ней только напоминание.
    """
    # Адрес — у последнего вызова смены состояния, а без него у последней работы в редакторе: игру
    # останавливают тем каналом, которым её запускали.
    server = url = state_server = state_url = ""
    stateful = launched = False
    background, finished = [], set()
    try:
        fh = open(path, "r", errors="ignore")
    except OSError:
        return server, url, stateful, launched, False

    with fh:
        for line in fh:
            if "<tool-use-id>" in line:
                for tid in background:
                    if "<tool-use-id>%s</tool-use-id>" % tid in line:
                        finished.add(tid)

            if '"tool_use"' not in line:
                continue

            try:
                event = json.loads(line)
            except ValueError:
                continue

            message = event.get("message")
            content = message.get("content") if isinstance(message, dict) else None
            if not isinstance(content, list):
                continue

            for block in content:
                if not isinstance(block, dict) or block.get("type") != "tool_use":
                    continue

                called = str(block.get("name") or "")
                given = block.get("input")
                given = given if isinstance(given, dict) else {}
                parts = called.split("__")
                if len(parts) > 2 and parts[0] == "mcp" and is_editor_tool(parts[2]):
                    server, url = parts[1], ""
                    if parts[2] == STATE:
                        state_server, state_url = parts[1], ""
                        stateful, launched = True, truthy(given.get("isPlaying"))
                elif called == "Bash":
                    command = given.get("command") or ""
                    found = cli_any(command)
                    if found:
                        server, url = "", found
                    seen, state_found, launch = cli_state(command)
                    if seen and state_found:
                        state_server, state_url = "", state_found
                        stateful, launched = True, launch
                elif called == "Agent" and given.get("run_in_background") is not False and block.get("id"):
                    # Исполнитель по умолчанию фоновый: его конец приходит уведомлением с id вызова.
                    background.append(block["id"])

    live = any(tid not in finished for tid in background)
    if stateful:
        server, url = state_server, state_url
    return server, url, stateful, launched, live


def address(event, server, url):
    """Адрес MCP-сервера: у канала CLI он стоит в самой команде, у канала MCP берётся из
    `.mcp.json` проекта сессии — файл один на оба проекта, реестр адресов у каждого свой."""
    if url:
        return url

    for base in (os.environ.get("CLAUDE_PROJECT_DIR") or "", event.get("cwd") or "",
                 os.path.realpath(os.path.join(sys.argv[1], "..", ".."))):
        if not base:
            continue
        try:
            with open(os.path.join(base, ".mcp.json"), encoding="utf-8") as fh:
                return json.load(fh)["mcpServers"][server]["url"]
        except Exception:
            continue

    return ""


def rpc(url, payload, sid, deadline):
    req = urllib.request.Request(url, data=json.dumps(payload).encode("utf-8"), method="POST")
    req.add_header("Content-Type", "application/json")
    req.add_header("Accept", "application/json, text/event-stream")
    if sid:
        req.add_header("Mcp-Session-Id", sid)
    left = deadline - time.monotonic()
    if left <= 0:
        raise TimeoutError("исчерпан бюджет ожидания")
    with OPENER.open(req, timeout=min(2.0, left)) as r:
        return r.headers, r.read().decode("utf-8", "replace")


def payload(text):
    """Тело HTTP-транспорта приходит SSE-строкой `data: {...}`, stdio-транспорта — голым JSON."""
    for line in text.splitlines():
        if line.startswith("data:"):
            try:
                return json.loads(line[5:].strip())["result"]["structuredContent"]["result"]
            except Exception:
                pass
    return json.loads(text)["result"]["structuredContent"]["result"]


def session(url, deadline):
    headers, _ = rpc(url, {"jsonrpc": "2.0", "id": 1, "method": "initialize",
                           "params": {"protocolVersion": "2024-11-05", "capabilities": {},
                                      "clientInfo": {"name": "unity-playmode-stop-reminder",
                                                     "version": "1"}}}, None, deadline)
    sid = headers.get("Mcp-Session-Id")
    if sid:
        rpc(url, {"jsonrpc": "2.0", "method": "notifications/initialized"}, sid, deadline)
    return sid


def is_playing(url):
    """Редактор отвечает, идёт ли игра. Не ответил — None: молчим, а не гадаем."""
    deadline = time.monotonic() + BUDGET
    try:
        sid = session(url, deadline)
        _, body = rpc(url, {"jsonrpc": "2.0", "id": 2, "method": "tools/call",
                            "params": {"name": READ, "arguments": {}}}, sid, deadline)
        return bool(payload(body).get("IsPlaying"))
    except Exception:
        return None


def stop_playing(url):
    """Остановить игру тем же тулом, которым её запускал вызывающий. Не вышло — False."""
    deadline = time.monotonic() + BUDGET
    try:
        sid = session(url, deadline)
        rpc(url, {"jsonrpc": "2.0", "id": 3, "method": "tools/call",
                  "params": {"name": STATE, "arguments": {"isPlaying": False}}}, sid, deadline)
        return True
    except Exception:
        return False


def settled(url):
    """Остановка игры асинхронна: ждём, пока редактор перестанет отвечать, что она идёт."""
    until = time.monotonic() + BUDGET
    while time.monotonic() < until:
        if is_playing(url) is not True:
            return True
        time.sleep(0.3)
    return False


def close_editor(url):
    """Закрыть редактор: (закрыт ли, причина отказа). Ответ «не закрыт: …» — выход не шёл; обрыв
    соединения — выход шёл, итог подтверждает редактор, переставший отвечать."""
    deadline = time.monotonic() + CLOSE_BUDGET
    try:
        sid = session(url, deadline)
        left = deadline - time.monotonic()
        req = urllib.request.Request(url, data=json.dumps({
            "jsonrpc": "2.0", "id": 4, "method": "tools/call",
            "params": {"name": EXEC, "arguments": {"csharpCode": CLOSE_CODE, "className": "Script",
                                                   "methodName": "Main"}}}).encode("utf-8"),
            method="POST")
        req.add_header("Content-Type", "application/json")
        req.add_header("Accept", "application/json, text/event-stream")
        if sid:
            req.add_header("Mcp-Session-Id", sid)
        with OPENER.open(req, timeout=max(left, 0.1)) as r:
            body = r.read().decode("utf-8", "replace")
        result = payload(body)
        value = result.get("value") if isinstance(result, dict) else result
        if isinstance(value, str) and value.startswith("не закрыт"):
            return False, value
    except Exception:
        pass

    until = time.monotonic() + BUDGET
    while time.monotonic() < until:
        if is_playing(url) is None:
            return True, ""
        time.sleep(0.5)
    return False, "редактор после выхода продолжает отвечать"


try:
    d = json.loads(os.environ.get("HOOK_INPUT", ""))
except Exception:
    sys.exit(0)

event = d.get("hook_event_name") or ""
if event not in ("Stop", "SubagentStop", "PreToolUse"):
    sys.exit(0)

# Шаговый вход один — вопрос человеку. Прочие шаги (правка файла, запуск агента, долгая команда)
# кончаются сами, и опрос живого редактора на каждом из них стоил бы сетевого вызова за шаг; окно
# же ждёт человека и не кончится, пока тот не ответит, — цена одного опроса там оправдана сроком.
if event == "PreToolUse" and leaving_step(d) != "wait":
    sys.exit(0)

# Конец прохода субагента без его идентификатора: транскрипт тогда родительский, эпизод чужой.
if event == "SubagentStop" and not d.get("agent_id"):
    sys.exit(0)

path = caller_transcript(d)
if not path:
    sys.exit(0)

server, url, stateful, launched, live = scan(path)
if not server and not url:
    sys.exit(0)

url = address(d, server, url)
if not url:
    sys.exit(0)

playing = is_playing(url)
if playing is None:
    sys.exit(0)

waiting = event == "PreToolUse"
moment = ("перед вопросом человеку: ответа он ждёт сколько угодно, и всё это время"
          if waiting else "на завершении ответа: до следующего сообщения")
said = []

# Игру запустил этот вызывающий и сам не остановил — хук останавливает её: замер отдачи показал, что
# напоминание на завершении ответа почти не исполняется, а висящий Play Mode ест ресурсы машины до
# следующего сообщения. Канон клиента разрешает трогать только СВОЮ игру: последний вызов смены
# состояния у вызывающего — запуск, значит игра его. Последним была остановка — игру перезапустил
# кто-то другой, её не трогаем и лишь называем наблюдаемое.
if playing and launched and stop_playing(url):
    said.append("Play Mode остановлен хуком " + moment + " игра висела бы без присмотра: игру "
                "запустил этот вызывающий и не остановил. Останавливать сразу по сбору данных "
                "остаётся за вызывающим — канон клиента /mnt/c/Unity/release/CLAUDE.md, «Вход в игру».")
    playing = not settled(url)
elif playing and stateful:
    said.append("Напоминание: редактор отвечает, что Play Mode ИДЁТ, а вызывающий уходит "
                + ("в ожидание ответа человека" if waiting else "с завершением ответа")
                + " — игра остаётся без присмотра. Кто её запустил, состояние редактора не "
                  "называет. Что с этим делать — канон клиента /mnt/c/Unity/release/CLAUDE.md, "
                  "«Вход в игру».")

if not playing:
    if live:
        said.append("Unity Editor открыт и хуком НЕ закрыт: у вызывающего идёт фоновый исполнитель, "
                    "который может работать в этом редакторе. Закрыть по его концу — свод `unity`.")
    else:
        closed, why = close_editor(url)
        if closed:
            said.append("Unity Editor закрыт хуком " + moment + " он держал бы память машины: "
                        "вызывающий в нём работал. Следующая работа в редакторе запускает его "
                        "заново, гасить по концу пачки работы остаётся за вызывающим — свод `unity`.")
        else:
            said.append("Unity Editor открыт и хуком НЕ закрыт (" + why + "): довести своё до "
                        "сохранения и закрыть — свод `unity`.")

if said:
    print(json.dumps({"hookSpecificOutput": {"hookEventName": event,
                                             "additionalContext": " ".join(said)}},
                     ensure_ascii=False))
PY
exit 0
