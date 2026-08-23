#!/bin/bash
# PreToolUse hook (mcp__ai-game-developer__editor-application-set-state): запуск Play Mode при
# несохранённой сцене отклоняется. Модальное окно «Scene(s) Have Been Modified» блокирует главный
# поток редактора, а MCP-плагин исполняет в нём все вызовы — после появления окна не проходит ни
# один тул, включая закрывающий его; снимает только человек кнопкой в редакторе. Дисциплинарное
# правило клиентского канона от этого не удержало — гейт механический.
# Срабатывает только на isPlaying=true: остановка Play Mode и пауза окна не дают, сети не касаются.
# Состояние спрашивается у самого Unity (scene-list-opened → IsDirty); адрес сервера читается из
# .mcp.json ПРОЕКТА СЕССИИ по имени, взятому из tool_name: файл один на оба проекта (серверный и
# клиентский), а реестр адресов у каждого свой — переезд порта правится в реестре своего проекта.
# Регистрируется в обоих проектах абсолютным путём и запускается через `bash`: на диске Windows
# бит исполнения не держится, маунт его сбрасывает.
# Уже идущий Play Mode пропускается: повторный запуск окна не даёт, а IsDirty в Play Mode относится
# к временной копии сцены и о редакторской не говорит ничего.
# Unity не ответил (Editor выключен, плагин не поднят, таймаут) — вызов НЕ блокируется: инжектится
# строка о непройденной проверке, иначе хук ронял бы работу при просто выключенном редакторе.

input=$(cat)

if ! command -v python3 >/dev/null 2>&1; then
  # Разобрать вход нечем: сузить до запуска Play Mode грубым признаком, чтобы не шуметь на остановке.
  case "$input" in
    *'"isPlaying"'*[Tt]rue*)
      echo '{"hookSpecificOutput":{"hookEventName":"PreToolUse","additionalContext":"unity-playmode-guard: проверка состояния сцен не выполнена — в окружении нет python3. Запуск Play Mode не блокирован. Проверь IsDirty сам (scene-list-opened): вход в Play Mode при несохранённой сцене даёт модальное окно, снимаемое только человеком."}}'
      ;;
  esac
  exit 0
fi

self="$(cd "$(dirname "$0")/../.." 2>/dev/null && pwd)"

HOOK_INPUT="$input" python3 - "${CLAUDE_PROJECT_DIR:-}" "$self" <<'PY'
import json, os, sys, time, urllib.request

PROJECT_DIR, SELF_ROOT = sys.argv[1], sys.argv[2]
DEADLINE = time.monotonic() + 7.0
OPENER = urllib.request.build_opener(urllib.request.ProxyHandler({}))

MODAL = ("Вход в Play Mode при несохранённой сцене даёт модальное окно «Scene(s) Have Been "
         "Modified». Оно блокирует главный поток редактора, и дальше не проходит ни один вызов "
         "MCP, включая закрывающий само окно. Снять его может только человек кнопкой в редакторе.")


def context(msg):
    print(json.dumps({"hookSpecificOutput": {"hookEventName": "PreToolUse",
                                             "additionalContext": "unity-playmode-guard: " + msg}},
                     ensure_ascii=False))
    sys.exit(0)


def unchecked(reason):
    context("проверить состояние сцен не удалось (%s). Запуск Play Mode не блокирован. Проверь "
            "IsDirty сам (scene-list-opened). %s" % (reason, MODAL))


try:
    d = json.loads(os.environ.get("HOOK_INPUT", ""))
except Exception:
    sys.exit(0)

playing = (d.get("tool_input") or {}).get("isPlaying")
if isinstance(playing, str):
    playing = playing.strip().lower() in ("true", "1", "yes")
if not playing:
    sys.exit(0)

# Имя сервера берётся у сработавшего тула, а не литералом: у хука и матчера один источник.
parts = (d.get("tool_name") or "").split("__")
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
    sys.exit(0)

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
sys.stderr.write(
    "[unity-playmode-guard] Запуск Play Mode отклонён: несохранённые изменения в сценах — %s.\n"
    "%s\n"
    "Изменения твои → сохранить (scene-save) и повторить запуск.\n"
    "Изменения не твои (параллельная сессия либо человек) → Play Mode не запускать вовсе, сцену не "
    "сохранять и не выгружать, задачу отложить, пользователю сказать, что редактор занят.\n"
    % (", ".join(dirty), MODAL))
sys.exit(2)
PY
