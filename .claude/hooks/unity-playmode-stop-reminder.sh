#!/bin/bash

# Ведомая копия. Источник — /var/www/html/game/.claude/hooks/unity-playmode-stop-reminder.sh в
# серверном репозитории; копию перезаписывает его хук при каждой правке источника. Правка копии
# теряется молча — вносить её в источник. Серверного репозитория нет под рукой → назвать нужную
# правку пользователю.

# Stop|SubagentStop hook: напоминание об идущем Play Mode на завершении ответа. Момент один — ответ
# закончился, а игра идёт: до следующего сообщения она висит без присмотра.
# Шаговых входов (правка файла, запуск агента, долгая команда) у хука нет: состояние берётся
# опросом живого редактора, и на каждой правке файла тот стоил бы сетевого вызова за правку.
# Момент ВХОДА в игру держит гейт клиентского репозитория unity-playmode-guard.sh: он стоит на
# самом вызове запуска и спрашивает то же состояние. Момент первого вызова тула редактора и
# момент запуска игры («остановить по сбору данных») держит unity-skill-reminder.sh.
#
# Состояние даёт САМ редактор (`editor-application-get-state`), не транскрипт: игру останавливает
# кто угодно мимо хука — человек в редакторе, параллельная сессия, — и счёт по транскрипту
# расходился бы с действительностью молча. Редактор не ответил (Editor выключен, плагин не поднят,
# таймаут) — хук молчит: о недоступности редактора шуметь нечем.
# Канал опроса — прямой JSON-RPC по адресу MCP-сервера. `npx unity-mcp-cli run-tool` отдаёт то же
# состояние за 2.9 с против 0.25 с у полного вызова отсюда (замер 05.09.2026), а цена платится на
# КАЖДОМ завершении ответа Unity-сессии. Запрет свода `unity` на сырое рукопожатие — о канале
# работы агента, не о коде хуков, ходящих по тому же порту. Тело опроса повторяет
# unity-playmode-guard.sh осознанно: тот живёт в клиентском репозитории и уезжает вместе с ним, а
# общий модуль `lib/` доезжает туда лишь как зависимость ЗЕРКАЛИРУЕМОГО скрипта — связь косвенная.
# Разбор CLI-вызова оба берут вопреки ей общим носителем (`lib/unity_cli.py`): расхождение форм у
# двух каналов ОДНОГО вызова молчаливо, а обрыв самой связи гейт называет вслух пометкой.
#
# Автора запуска хук не называет: `editor-application-get-state` отдаёт сам факт игры, без автора.
# Текст сообщает наблюдаемое и отсылает к канону клиента (/mnt/c/Unity/release/CLAUDE.md, «Вход в
# игру»): случаи «своя» и «не своя» разводит он, копия критерия в хуке разошлась бы с ним молча.
# Канон не наследуется ни одной сессией и доезжает только явным чтением — оттого его адрес стоит в
# самом напоминании.
#
# ЦЕНУ опроса гейтит транскрипт ВЫЗЫВАЮЩЕГО (lib/transcript.py, caller_transcript): опрос идёт,
# только когда вызывающий сам звал смену состояния редактора — тулом MCP-сервера (субагент,
# которому сервер подключён) либо командой `npx unity-mcp-cli run-tool` (главная сессия, у которой
# тулов этого сервера нет). Состояния признак не даёт и дать не может: он лишь повод спросить
# редактор. Вызывающий игру не запускал — по канону она ему «не своя», трогать её нельзя, и
# напоминание было бы шумом. Тем же вызовом берётся АДРЕС опроса: у канала CLI — `--url` самой
# команды, у канала MCP — имя сервера из имени тула плюс `.mcp.json` проекта сессии (файл один на
# оба проекта, реестр адресов у каждого свой). Команда опознаётся по ПОЗИЦИИ имени в части цепочки
# (lib/unity_cli.py поверх lib/chain_parser.py), не по вхождению его в текст: тем же текстом вызов
# ходит аргументом чужой команды — телом heredoc, шаблоном поиска, фикстурой набора самопроверки.
# Носитель опознания общий с гейтом входа: тот стоит на ТОМ ЖЕ вызове, и вторая копия разбора
# разошлась бы с ним молча — форма, видная одному, второму невидима.
# `SubagentStop` без `agent_id` — молчание: свой эпизод адресовать нечем, а `transcript_path` ведёт
# тогда в чужой транскрипт.
# Маркера-троттлинга нет: состояние наблюдаемое, напоминание идёт, пока идёт игра, — остановят её
# хоть мимо хука, следующий опрос вернёт false и хук смолкнет сам.
# Регистрируется в настройках ОБОИХ проектов абсолютным путём на Stop|SubagentStop: Play Mode
# запускают и серверная сессия, и работающая из репозитория клиента, а настройки каждая читает свои.
# Не блокирует: инжектит additionalContext под `hookEventName` СВОЕГО события — поле из схемы
# соседнего программа отбрасывает молча. `decision: "block"` тут негоден: находка ШТАТНАЯ,
# состояния под чужое действие не несёт (skill `gate-mechanics`, «Канал ДОКЛАДА хука»).

input=$(cat)
hooks_dir="$(cd "$(dirname "$0")" && pwd)"

command -v python3 >/dev/null 2>&1 || exit 0

HOOK_INPUT="$input" python3 - "$hooks_dir" <<'PY' 2>/dev/null
import json, os, sys, time, urllib.request

sys.path.insert(0, os.path.join(sys.argv[1], "lib"))
from transcript import caller_transcript
from unity_cli import tool_calls

# Тул смены состояния редактора: MCP-канал зовёт его именем тула, CLI-канал — позиционным
# аргументом команды оболочки. Читающий тул даёт состояние, менять его не может — поводом не идёт.
STATE = "editor-application-set-state"
READ = "editor-application-get-state"
BUDGET = 3.0

OPENER = urllib.request.build_opener(urllib.request.ProxyHandler({}))


def cli_url(command):
    """Адрес сервера у вызова смены состояния через CLI; такого вызова нет — пустая строка."""
    found = ""
    for opts in tool_calls(command, STATE):
        found = opts.get("--url") or found

    return found


def probe_address(path):
    """Адрес опроса, взятый у ПОСЛЕДНЕГО вызова смены состояния в транскрипте вызывающего:
    (имя MCP-сервера, адрес из команды CLI). Вызовов не было — обе части пусты, опрос не идёт.
    """
    server = url = ""
    try:
        fh = open(path, "r", errors="ignore")
    except OSError:
        return server, url

    with fh:
        for line in fh:
            if STATE not in line:
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
                if called.endswith(STATE):
                    parts = called.split("__")
                    if len(parts) > 2 and parts[0] == "mcp":
                        server, url = parts[1], ""
                elif called == "Bash":
                    found = cli_url(given.get("command") or "")
                    if found:
                        server, url = "", found

    return server, url


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


def is_playing(url):
    """Редактор отвечает, что игра идёт. Не ответил — None: молчим, а не гадаем."""
    deadline = time.monotonic() + BUDGET
    try:
        headers, _ = rpc(url, {"jsonrpc": "2.0", "id": 1, "method": "initialize",
                               "params": {"protocolVersion": "2024-11-05", "capabilities": {},
                                          "clientInfo": {"name": "unity-playmode-stop-reminder",
                                                         "version": "1"}}}, None, deadline)
        sid = headers.get("Mcp-Session-Id")
        if sid:
            rpc(url, {"jsonrpc": "2.0", "method": "notifications/initialized"}, sid, deadline)
        _, body = rpc(url, {"jsonrpc": "2.0", "id": 2, "method": "tools/call",
                            "params": {"name": READ, "arguments": {}}}, sid, deadline)
        return bool(payload(body).get("IsPlaying"))
    except Exception:
        return None


try:
    d = json.loads(os.environ.get("HOOK_INPUT", ""))
except Exception:
    sys.exit(0)

event = d.get("hook_event_name") or ""
if event not in ("Stop", "SubagentStop"):
    sys.exit(0)

# Конец прохода субагента без его идентификатора: транскрипт тогда родительский, эпизод чужой.
if event == "SubagentStop" and not d.get("agent_id"):
    sys.exit(0)

path = caller_transcript(d)
if not path:
    sys.exit(0)

server, url = probe_address(path)
if not server and not url:
    sys.exit(0)

url = address(d, server, url)
if not url or is_playing(url) is not True:
    sys.exit(0)

print(json.dumps({"hookSpecificOutput": {
    "hookEventName": event,
    "additionalContext": "Напоминание: редактор отвечает, что Play Mode ИДЁТ, а ответ завершается "
                         "— до следующего сообщения игра остаётся без присмотра. Кто её запустил, "
                         "состояние редактора не называет. Что с этим делать — канон клиента "
                         "/mnt/c/Unity/release/CLAUDE.md, «Вход в игру»."}},
                 ensure_ascii=False))
PY
exit 0
