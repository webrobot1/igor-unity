#!/bin/bash
# Прогон гейта unity-playmode-guard.sh на синтетических событиях PreToolUse:
#   BLOCK     — отказ обоими каналами разом: код 2 с текстом в stderr и `permissionDecision`
#               на stdout (ветвь, ради которой гейт заведён);
#   UNCHECKED — код 0 плюс additionalContext на stdout: проверка не прошла, вызов не блокирован;
#   PASS      — код 0 без вывода: случай не наш, вызов идёт дальше.
# Хуком НЕ является — ни в одних настройках не регистрируется, запускается вручную:
#   bash /mnt/c/Unity/release/.claude/hooks/unity-playmode-guard.test.sh
# Лежит рядом с хуком, а не в серверном проекте: хук ведёт клиентский репозиторий и уезжает
# вместе с ним — набор обязан ехать тем же грузом, иначе у получателя клиента гейт есть, а
# проверить его нечем.
# Unity Editor и настоящий Unity MCP набору не нужны: адрес сервера хук читает из .mcp.json, и
# набор подставляет собственный HTTP-фейк во временном каталоге. Ни один кейс не трогает ни
# редактор, ни сцены, ни сеть за пределы localhost.
# Каналов входа у гейта ДВА, и кейсы идут по каждому: вызов тула MCP-сервера и команда оболочки
# `npx unity-mcp-cli run-tool`. Вторая ось кейсов — ФОРМА ЗАПИСИ команды: гейт меряет ПОЗИЦИЮ имени
# в части цепочки, и тихий пропуск живёт там, где перед ней стоит ещё слово — тело цикла, вторая
# часть цепочки. Зеркальная сторона — текст, вызовом НЕ являющийся: имя тула аргументом чужой
# команды и телом heredoc, поданным на ввод, обязано остаться незамеченным.
# Правится разбор события, порядок опроса Unity либо текст отказа — прогнать набор: тело хука
# читается правдоподобно и при сломанном разборе, а цена промаха двусторонняя. Тихий пропуск
# вешает редактор модальным окном, снять которое может только человек; лишний отказ рубит вход
# в Play Mode на чистых сценах. Оба видны только на прогоне.
# Новая ветвь разбора — новые кейсы обеих сторон: ей своё ожидание, соседней прежней — прежнее.

set -u

HOOKS="$(cd "$(dirname "$0")" && pwd)"
HOOK="$HOOKS/unity-playmode-guard.sh"
[ -f "$HOOK" ] || { echo "не найден $HOOK"; exit 1; }
command -v python3 >/dev/null 2>&1 || { echo "набору нужен python3 — им же работает и сам хук"; exit 1; }

T="$(mktemp -d)"
SRV=""
cleanup() { [ -n "$SRV" ] && kill "$SRV" 2>/dev/null; rm -rf "$T"; }
trap cleanup EXIT

PASS=0; FAIL=0
ok()  { PASS=$((PASS+1)); }
bad() { FAIL=$((FAIL+1)); echo "ПРОВАЛ [$1] $2"; }

# --- фейковый MCP-сервер: конфигурацию читает из файла на КАЖДОМ запросе, оттого инстанс один на
#     весь прогон, а кейс лишь переписывает cfg.json. Журнал вызовов отвечает на вопрос, ходил ли
#     хук в сеть вообще: у ветвей «случай не наш» он обязан остаться пустым.
cat > "$T/fake_mcp.py" <<'PY'
import json, os, sys, time
from http.server import BaseHTTPRequestHandler, HTTPServer

CFG, LOG, PORTFILE = sys.argv[1], sys.argv[2], sys.argv[3]


def cfg():
    try:
        with open(CFG, encoding="utf-8") as f:
            return json.load(f)
    except Exception:
        return {}


class H(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.0"

    def log_message(self, *a):
        pass

    def _send(self, body, sid):
        raw = json.dumps(body, ensure_ascii=False)
        if cfg().get("mode", "sse") == "sse":
            payload = ("data: " + raw + "\n\n").encode("utf-8")
            ctype = "text/event-stream"
        else:
            payload = raw.encode("utf-8")
            ctype = "application/json"
        self.send_response(200)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(payload)))
        if sid:
            self.send_header("Mcp-Session-Id", "fake-session")
        self.end_headers()
        self.wfile.write(payload)

    def _fail(self):
        self.send_response(500)
        self.send_header("Content-Length", "4")
        self.end_headers()
        self.wfile.write(b"boom")

    def do_POST(self):
        length = int(self.headers.get("Content-Length") or 0)
        try:
            req = json.loads(self.rfile.read(length).decode("utf-8", "replace"))
        except Exception:
            req = {}
        c = cfg()
        method = req.get("method") or "?"
        name = (req.get("params") or {}).get("name") or ""
        with open(LOG, "a", encoding="utf-8") as f:
            f.write(method + " " + name + "\n")
        if c.get("sleep"):
            time.sleep(float(c["sleep"]))

        if method == "initialize":
            self._send({"jsonrpc": "2.0", "id": req.get("id"),
                        "result": {"protocolVersion": "2024-11-05"}}, c.get("session", True))
            return
        if method == "notifications/initialized":
            self.send_response(202)
            self.send_header("Content-Length", "0")
            self.end_headers()
            return
        if method == "tools/call":
            if name == "editor-application-get-state":
                if c.get("state_fail"):
                    return self._fail()
                res = {"IsPlaying": bool(c.get("playing"))}
            elif name == "scene-list-opened":
                if c.get("scenes_fail"):
                    return self._fail()
                res = c.get("scenes", [])
            else:
                res = {}
            self._send({"jsonrpc": "2.0", "id": req.get("id"),
                        "result": {"structuredContent": {"result": res}}}, False)
            return
        self.send_response(404)
        self.send_header("Content-Length", "0")
        self.end_headers()


srv = HTTPServer(("127.0.0.1", 0), H)
with open(PORTFILE, "w", encoding="utf-8") as f:
    f.write(str(srv.server_port))
srv.serve_forever()
PY

printf '%s' '{}' > "$T/cfg.json"
: > "$T/calls.log"
python3 "$T/fake_mcp.py" "$T/cfg.json" "$T/calls.log" "$T/port" &
SRV=$!
for _ in 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20; do
  [ -s "$T/port" ] && break
  sleep 0.2
done
[ -s "$T/port" ] || { echo "фейковый MCP не поднялся"; exit 1; }
PORT="$(cat "$T/port")"

python3 -c '
import json, os, sys
port = sys.argv[1]
json.dump({"mcpServers": {"ai-game-developer": {"type": "http",
           "url": "http://127.0.0.1:%s/mcp" % port}}}, open(os.path.join(sys.argv[2], ".mcp.json"), "w"))
' "$PORT" "$T"

# Каталог с адресом на заведомо закрытый порт: ветвь «Unity не ответил».
mkdir -p "$T/dead"
python3 -c '
import json, os, sys
json.dump({"mcpServers": {"ai-game-developer": {"type": "http", "url": "http://127.0.0.1:1/mcp"}}},
          open(os.path.join(sys.argv[1], ".mcp.json"), "w"))
' "$T/dead"

cfg() { printf '%s' "$1" > "$T/cfg.json"; }

bash_event() { # bash_event <команда оболочки>
  CMD="$1" python3 -c '
import json, os
print(json.dumps({"hook_event_name": "PreToolUse", "tool_name": "Bash",
                  "tool_input": {"command": os.environ["CMD"]}}, ensure_ascii=False))
'
}

event() { # event <tool_name> <isPlaying как JSON-литерал | OMIT> [cwd]
  TOOL="$1" PLAY="$2" CWD="${3-}" python3 -c '
import json, os
ti = {}
if os.environ["PLAY"] != "OMIT":
    ti["isPlaying"] = json.loads(os.environ["PLAY"])
d = {"hook_event_name": "PreToolUse", "tool_name": os.environ["TOOL"], "tool_input": ti}
if os.environ.get("CWD"):
    d["cwd"] = os.environ["CWD"]
print(json.dumps(d, ensure_ascii=False))
'
}

call() { # call <событие> [project_dir]
  local ev="$1" pdir="${2-$T}"
  : > "$T/calls.log"
  STDOUT="$(printf '%s' "$ev" | CLAUDE_PROJECT_DIR="$pdir" bash "$HOOK" 2>"$T/stderr.txt")"
  CODE=$?
  STDERR="$(cat "$T/stderr.txt")"
  if [ "$CODE" = 2 ]; then
    VERDICT=BLOCK
  elif [ -n "$STDOUT" ]; then
    case "$STDOUT" in *'"additionalContext"'*) VERDICT=UNCHECKED;; *) VERDICT=OTHER;; esac
  else
    VERDICT=PASS
  fi
}

want() { # want <описание> <BLOCK|UNCHECKED|PASS>
  if [ "$VERDICT" = "$2" ]; then ok; else bad "$1" "ждали $2 получили $VERDICT (код $CODE)"; fi
}

says() { # says <описание> <текст> <подстрока>
  case "$2" in *"$3"*) ok;; *) bad "$1" "в выводе нет «$3»";; esac
}

silent_net() { # silent_net <описание> — в сеть не ходили вовсе
  if [ -s "$T/calls.log" ]; then bad "$1" "хук опрашивал Unity, хотя случай не его"; else ok; fi
}

touched_net() { # touched_net <описание> — в сеть ходили
  if [ -s "$T/calls.log" ]; then ok; else bad "$1" "хук Unity не опрашивал"; fi
}

CLEAN='{"mode":"sse","session":true,"scenes":[{"Name":"Main","IsDirty":false}]}'
DIRTY='{"mode":"sse","session":true,"scenes":[{"Name":"Main","IsDirty":true}]}'
TOOL=mcp__ai-game-developer__editor-application-set-state

# --- случай не наш: остановка Play Mode, пауза, отсутствие поля
cfg "$DIRTY"
call "$(event "$TOOL" false)"
want        "остановка Play Mode" PASS
silent_net  "остановка Play Mode"

call "$(event "$TOOL" OMIT)"
want        "поля isPlaying нет" PASS
silent_net  "поля isPlaying нет"

call "$(event "$TOOL" '"no"')"
want        "строка no" PASS
silent_net  "строка no"

# --- сцены чисты: запуск проходит, но состояние спрошено у Unity
cfg "$CLEAN"
call "$(event "$TOOL" true)"
want        "чистые сцены" PASS
touched_net "чистые сцены"

cfg '{"mode":"sse","session":true,"scenes":[]}'
call "$(event "$TOOL" true)"
want        "открытых сцен нет" PASS

# --- ветвь отказа: несохранённые сцены
cfg "$DIRTY"
call "$(event "$TOOL" true)"
want "несохранённая сцена"            BLOCK
says "несохранённая сцена: отказ"     "$STDERR" "Запуск Play Mode отклонён"
says "несохранённая сцена: имя"       "$STDERR" "Main"
says "несохранённая сцена: модалка"   "$STDERR" "Scene(s) Have Been Modified"
says "несохранённая сцена: свои"      "$STDERR" "scene-save"
says "несохранённая сцена: чужие"     "$STDERR" "редактор занят"
says "несохранённая сцена: канал JSON"  "$STDOUT" '"permissionDecision": "deny"'
says "несохранённая сцена: причина в JSON" "$STDOUT" "Запуск Play Mode отклонён"

cfg '{"mode":"sse","session":true,"scenes":[{"Name":"Main","IsDirty":true},{"Name":"UI","IsDirty":true}]}'
call "$(event "$TOOL" true)"
want "две несохранённые: отказ"       BLOCK
says "две несохранённые: первая"      "$STDERR" "Main"
says "две несохранённые: вторая"      "$STDERR" "UI"

cfg '{"mode":"sse","session":true,"scenes":[{"path":"Assets/Scenes/Level.unity","IsDirty":true}]}'
call "$(event "$TOOL" true)"
want "сцена без имени: отказ"         BLOCK
says "сцена без имени: путь"          "$STDERR" "Assets/Scenes/Level.unity"

cfg '{"mode":"sse","session":true,"scenes":[{"IsDirty":true}]}'
call "$(event "$TOOL" true)"
want "сцена без имени и пути"         BLOCK

cfg '{"mode":"sse","session":true,"scenes":[{"Name":"Main","IsDirty":false},{"Name":"UI","IsDirty":true}]}'
call "$(event "$TOOL" true)"
want "грязна лишь вторая: отказ"      BLOCK
says "грязна лишь вторая: имя"        "$STDERR" "UI"

# --- строковая форма флага: клиент вправе прислать его текстом
cfg "$DIRTY"
call "$(event "$TOOL" '"true"')"
want "строка true"  BLOCK
call "$(event "$TOOL" '"1"')"
want "строка 1"     BLOCK
call "$(event "$TOOL" '"yes"')"
want "строка yes"   BLOCK
call "$(event "$TOOL" '"True"')"
want "строка True"  BLOCK

# --- Play Mode уже идёт: вход в идущую игру канон запрещает, автор запуска ниоткуда не наблюдаем.
# Сцены тут любые: до их разбора дело не доходит, а IsDirty в Play Mode относится к временной копии.
cfg '{"mode":"sse","session":true,"playing":true,"scenes":[{"Name":"Main","IsDirty":true}]}'
call "$(event "$TOOL" true)"
want "игра уже идёт: отказ"            BLOCK
says "игра уже идёт: наблюдаемое"      "$STDERR" "УЖЕ ИДЁТ"
says "игра уже идёт: автор не назван"  "$STDERR" "состояние редактора не называет"
says "игра уже идёт: адрес канона"     "$STDERR" "/mnt/c/Unity/release/CLAUDE.md"
says "игра уже идёт: канал JSON"       "$STDOUT" '"permissionDecision": "deny"'

cfg '{"mode":"sse","session":true,"playing":true,"scenes":[{"Name":"Main","IsDirty":false}]}'
call "$(event "$TOOL" true)"
want "игра идёт при чистых сценах: отказ" BLOCK

# --- состояние редактора не отдалось: режим правки тут обычный случай, отказ остаётся в силе
cfg '{"mode":"sse","session":true,"state_fail":true,"scenes":[{"Name":"Main","IsDirty":true}]}'
call "$(event "$TOOL" true)"
want "состояние редактора не отдалось" BLOCK

# --- перечень сцен не отдался: блокировать нечем, вызов пропускается с пометкой
cfg '{"mode":"sse","session":true,"scenes_fail":true}'
call "$(event "$TOOL" true)"
want "перечень сцен не отдался"        UNCHECKED
says "перечень сцен: не блокирован"    "$STDOUT" "не блокирован"
says "перечень сцен: чем проверить"    "$STDOUT" "scene-list-opened"

# --- форма ответа: голый JSON stdio-транспорта наравне с SSE
cfg '{"mode":"json","session":true,"scenes":[{"Name":"Main","IsDirty":true}]}'
call "$(event "$TOOL" true)"
want "тело голым JSON" BLOCK

# --- сервер не отдал Mcp-Session-Id
cfg '{"mode":"sse","session":false,"scenes":[{"Name":"Main","IsDirty":true}]}'
call "$(event "$TOOL" true)"
want "без заголовка сессии" BLOCK

# --- адрес берётся из cwd события, когда каталога проекта в событии нет
cfg "$DIRTY"
call "$(event "$TOOL" true "$T")" ""
want "адрес из cwd события" BLOCK

# --- Unity не ответил: редактор выключен, плагин не поднят
call "$(event "$TOOL" true)" "$T/dead"
want "Unity не ответил"             UNCHECKED
says "Unity не ответил: пометка"    "$STDOUT" "не блокирован"
says "Unity не ответил: модалка"    "$STDOUT" "Scene(s) Have Been Modified"

# --- бюджет ожидания: висящий сервер не держит вызов дольше своего лимита
cfg '{"mode":"sse","session":true,"sleep":5,"scenes":[{"Name":"Main","IsDirty":true}]}'
START=$(date +%s)
call "$(event "$TOOL" true)"
ELAPSED=$(( $(date +%s) - START ))
want "висящий сервер"               UNCHECKED
if [ "$ELAPSED" -le 9 ]; then ok; else bad "висящий сервер: бюджет" "вызов держали $ELAPSED с"; fi
cfg "$DIRTY"

# --- имя сервера и его адрес
call "$(event "mcp__unity-absent-server__editor-application-set-state" true)"
want "сервера нет в .mcp.json"        UNCHECKED
says "сервера нет: назван поимённо"   "$STDOUT" "unity-absent-server"

call "$(event "editor-application-set-state" true)"
want "имя тула не MCP-формы"          UNCHECKED
says "имя тула не MCP-формы: причина" "$STDOUT" "нет имени MCP-сервера"

# --- канал CLI: тот же тул зовётся командой оболочки у сессии, которой тулы сервера не поданы.
# Адрес редактора стоит в самой команде: `.mcp.json` тут не при чём, и каталог проекта кейсам не
# нужен.
URL="http://127.0.0.1:$PORT/mcp"
RUN="npx unity-mcp-cli run-tool editor-application-set-state"

cfg "$DIRTY"
call "$(bash_event "$RUN --input '{\"isPlaying\":true}' --url $URL")"
want "CLI: запуск при несохранённой"        BLOCK
says "CLI: запуск — имя сцены"              "$STDERR" "Main"
says "CLI: запуск — канал JSON"             "$STDOUT" '"permissionDecision": "deny"'

call "$(bash_event "$RUN --input={\"isPlaying\":true} --url=$URL")"
want "CLI: форма --флаг=значение"           BLOCK

call "$(bash_event "cd /tmp && $RUN --input '{\"isPlaying\":true}' --url $URL")"
want "CLI: вторая часть цепочки"            BLOCK

call "$(bash_event "for i in 1; do $RUN --input '{\"isPlaying\":true}' --url $URL ; done")"
want "CLI: тело цикла"                      BLOCK

call "$(bash_event "unity-mcp-cli run-tool editor-application-set-state --input '{\"isPlaying\":true}' --url $URL")"
want "CLI: вызов без npx"                   BLOCK

# `--input` не разобран как JSON — флаг читается по сырому тексту параметра. Значение приходит
# переменной оболочки либо теряет кавычки на разборе команды: запуск обязан быть отбит, явная
# остановка — пропущена, иначе гейт рубит саму остановку игры.
call "$(bash_event "$RUN --input 'сломанный json' --url $URL")"
want "CLI: --input не разобран"             BLOCK

call "$(bash_event "$RUN --url $URL")"
want "CLI: --input не передан"              BLOCK

call "$(bash_event "$RUN --input \"\$JSON\" --url $URL")"
want "CLI: --input переменной оболочки"     BLOCK

call "$(bash_event "$RUN --input {\"isPlaying\":true} --url $URL")"
want "CLI: запуск без кавычек JSON"         BLOCK

cfg "$DIRTY"
call "$(bash_event "$RUN --input {\"isPlaying\":false} --url $URL")"
want       "CLI: остановка без кавычек JSON"  PASS
silent_net "CLI: остановка без кавычек JSON"

call "$(bash_event "$RUN --input={\"isPlaying\":false} --url=$URL")"
want       "CLI: остановка формой --флаг=значение" PASS
silent_net "CLI: остановка формой --флаг=значение"

cfg '{"mode":"sse","session":true,"playing":true,"scenes":[{"Name":"Main","IsDirty":false}]}'
call "$(bash_event "$RUN --input '{\"isPlaying\":true}' --url $URL")"
want "CLI: игра уже идёт"                   BLOCK
says "CLI: игра уже идёт — наблюдаемое"     "$STDERR" "УЖЕ ИДЁТ"

# --- канал CLI: случай не наш — в сеть не ходим вовсе
cfg "$DIRTY"
call "$(bash_event "$RUN --input '{\"isPlaying\":false}' --url $URL")"
want       "CLI: остановка"                 PASS
silent_net "CLI: остановка"

call "$(bash_event "npx unity-mcp-cli run-tool editor-application-get-state --url $URL")"
want       "CLI: читающий тул"              PASS
silent_net "CLI: читающий тул"

call "$(bash_event "grep -n \"$RUN\" /dev/null")"
want       "CLI: имя тула аргументом чужой команды" PASS
silent_net "CLI: имя тула аргументом чужой команды"

call "$(bash_event "cat > $T/fixture.txt <<'EOF'
$RUN --input '{\"isPlaying\":true}' --url $URL
EOF")"
want       "CLI: тело heredoc данными"      PASS
silent_net "CLI: тело heredoc данными"

cfg "$CLEAN"
call "$(bash_event "$RUN --input '{\"isPlaying\":true}' --url $URL")"
want        "CLI: чистые сцены"             PASS
touched_net "CLI: чистые сцены"

# --- адрес редактора: у канала CLI он есть только в самой команде
cfg "$DIRTY"
call "$(bash_event "$RUN --input '{\"isPlaying\":true}'")"
want "CLI: без --url"                       UNCHECKED
says "CLI: без --url — причина"             "$STDOUT" "адрес редактора"
silent_net "CLI: без --url"

# --- общий носитель разбора команды не доехал в контур: гейт не молчит и не падает, а помечает.
# В клиентский контур lib/unity_cli.py везёт зеркалирование серверного репозитория — связь
# косвенная, и её обрыв обязан быть слышен на самом вызове.
mkdir -p "$T/nolib"
cp "$HOOK" "$T/nolib/"
: > "$T/calls.log"
STDOUT="$(printf '%s' "$(bash_event "$RUN --input '{\"isPlaying\":true}' --url $URL")" \
  | CLAUDE_PROJECT_DIR="$T" bash "$T/nolib/$(basename "$HOOK")" 2>"$T/stderr.txt")"
CODE=$?
case "$STDOUT" in *'"additionalContext"'*) VERDICT=UNCHECKED;; *) VERDICT=PASS;; esac
want       "CLI: модуль разбора не доехал"  UNCHECKED
says       "CLI: модуль разбора — назван"   "$STDOUT" "lib/unity_cli.py"
silent_net "CLI: модуль разбора не доехал"

# --- битый вход: разбирать нечего, работу не ронять
: > "$T/calls.log"
STDOUT="$(printf 'не json вовсе' | CLAUDE_PROJECT_DIR="$T" bash "$HOOK" 2>"$T/stderr.txt")"; CODE=$?
if [ "$CODE" = 0 ] && [ -z "$STDOUT" ]; then ok; else bad "вход не JSON" "код $CODE, вывод «$STDOUT»"; fi
silent_net "вход не JSON"

STDOUT="$(printf '' | CLAUDE_PROJECT_DIR="$T" bash "$HOOK" 2>"$T/stderr.txt")"; CODE=$?
if [ "$CODE" = 0 ] && [ -z "$STDOUT" ]; then ok; else bad "вход пуст" "код $CODE, вывод «$STDOUT»"; fi

STDOUT="$(printf '{"tool_name":"%s"}' "$TOOL" | CLAUDE_PROJECT_DIR="$T" bash "$HOOK" 2>"$T/stderr.txt")"; CODE=$?
if [ "$CODE" = 0 ] && [ -z "$STDOUT" ]; then ok; else bad "события без tool_input" "код $CODE, вывод «$STDOUT»"; fi

# --- окружение без python3: разбирать вход нечем, но запуск Play Mode всё равно помечается
mkdir -p "$T/bin"
for b in bash cat dirname; do ln -sf "$(command -v "$b")" "$T/bin/$b"; done
nopy() { # nopy <событие>
  : > "$T/calls.log"
  STDOUT="$(printf '%s' "$1" | env -i PATH="$T/bin" CLAUDE_PROJECT_DIR="$T" "$T/bin/bash" "$HOOK" 2>"$T/stderr.txt")"
  CODE=$?
}
nopy "$(event "$TOOL" true)"
if [ "$CODE" = 0 ]; then ok; else bad "без python3: запуск не блокирован" "код $CODE"; fi
says       "без python3: пометка"  "$STDOUT" "нет python3"
says       "без python3: модалка"  "$STDOUT" "несохранённой сцене"
silent_net "без python3"

nopy "$(event "$TOOL" false)"
if [ "$CODE" = 0 ] && [ -z "$STDOUT" ]; then ok; else bad "без python3: остановка тиха" "код $CODE, вывод «$STDOUT»"; fi

nopy "$(bash_event "$RUN --input '{\"isPlaying\":true}' --url $URL")"
says       "без python3: CLI помечен"   "$STDOUT" "нет python3"
silent_net "без python3: CLI"

nopy "$(bash_event "$RUN --input '{\"isPlaying\":false}' --url $URL")"
if [ "$CODE" = 0 ] && [ -z "$STDOUT" ]; then ok; else bad "без python3: CLI-остановка тиха" "код $CODE, вывод «$STDOUT»"; fi

# --- событие мимо гейта: отсев до запуска интерпретатора
call "$(bash_event "ls -la /tmp")"
want       "команда не про Play Mode"   PASS
silent_net "команда не про Play Mode"

# --- регистрация: хук молчит и при исправном теле, если его не позвали. Файл один на два контура,
#     копий нет — регистрацию держат ОБА реестра настроек, и снятие одной стороны бесшумно.
REG=$(HOOK="$HOOK" python3 -c '
import json, os, sys

hook = os.environ["HOOK"]
base = os.path.basename(hook)
targets = [("клиент", os.path.normpath(os.path.join(os.path.dirname(hook), "..", "settings.json"))),
           ("сервер", "/var/www/html/game/.claude/settings.json")]
bad, skipped = [], []

for label, path in targets:
    if not os.path.isfile(path):
        skipped.append(label + ": " + path + " не читается")
        continue
    try:
        settings = json.load(open(path, encoding="utf-8"))
    except Exception as e:
        bad.append(label + ": настройки не разбираются как JSON (%s)" % e)
        continue
    found = []
    for groups in (settings.get("hooks") or {}).values():
        for group in groups:
            for entry in group.get("hooks") or []:
                command = entry.get("command") or ""
                if base in command:
                    found.append((group.get("matcher") or "", command))
    if not found:
        bad.append(label + ": хук не зарегистрирован ни на одном событии — гейт молчит")
        continue
    for matcher, command in found:
        if hook not in command:
            bad.append(label + ": зарегистрирован путь мимо файла — «%s»" % command)
        elif not command.split(hook)[0].strip().endswith("bash"):
            # Диск Windows бита исполнения не держит: без явного `bash` вызов падает молча.
            bad.append(label + ": хук зарегистрирован без запуска через bash — «%s»" % command)
        if "editor-application-set-state" not in matcher:
            bad.append(label + ": матчер «%s» не ловит запуск Play Mode тулом MCP" % matcher)
        # Второй канал того же запуска — команда оболочки `npx unity-mcp-cli run-tool`: у сессии
        # без тулов сервера он единственный, и матчер без Bash её вызов не видит.
        if "Bash" not in matcher.split("|"):
            bad.append(label + ": матчер «%s» не ловит запуск Play Mode командой оболочки" % matcher)

print("|".join(bad))
sys.stderr.write("\n".join(skipped))
' 2>"$T/reg-skipped.txt")
if [ -n "$REG" ]; then bad "регистрация" "$REG"; else ok; fi
SKIPPED="$(cat "$T/reg-skipped.txt")"
[ -n "$SKIPPED" ] && echo "ПРОПУЩЕНО [регистрация] $SKIPPED"

echo "ИТОГ: пройдено $PASS, провалено $FAIL"
[ "$FAIL" = 0 ]
