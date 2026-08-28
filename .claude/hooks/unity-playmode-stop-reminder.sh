#!/bin/bash

# Ведомая копия. Источник — /var/www/html/game/.claude/hooks/unity-playmode-stop-reminder.sh в
# серверном репозитории; копию перезаписывает его хук при каждой правке источника. Правка копии
# теряется молча — вносить её в источник. Серверного репозитория нет под рукой → назвать нужную
# правку пользователю.

# PreToolUse hook (Edit|Write|Bash|Agent|Task): напоминание об ОСТАВЛЕННОМ Play Mode — перед шагом,
# который Unity НЕ использует, при игре, запущенной ТЕМ ЖЕ вызывающим. Шаг — момент срабатывания,
# не критерий остановки. Сам критерий текст не несёт: он лежит в каноне клиента
# (/mnt/c/Unity/release/CLAUDE.md, «Вход в игру»), копия расходилась бы с ним молча — хук даёт
# состояние Play Mode и шаг, решение остаётся за правилом. Канон не наследуется ни одной сессией и
# доезжает только явным чтением — оттого его адрес стоит в самом напоминании.
# Момент ЗАПУСКА игры держит unity-skill-reminder.sh («остановить по сбору данных», одно на эпизод);
# тут ВТОРОЙ момент — уход вызывающего в другую работу при уже запущенной игре.
#
# Точки входа и их повод:
#   - правка файла — вызывающий ушёл в код; повод сильнее браузерного: по канону клиента правка C#
#     при запущенном Play Mode выбрасывает из игры, и висящая сессия входа теряется впустую;
#   - запуск агента — он работает минутами, игра всё это время висит без присмотра;
#   - долгая команда оболочки — прогон тестов, анализатор, фон.
# Вызовы тулов редактора и прогон тестов Unity точками входа НЕ идут: это сама работа с Unity, а не
# уход от неё. По той же причине из точек входа снята команда оболочки, зовущая Unity-CLI: она канал
# вызова тула, а её долгий timeout иначе дал бы напоминание прямо перед вызовом, ОСТАНАВЛИВАЮЩИМ
# игру. Ярлык шага считает общий носитель (lib/transcript.py, leaving_step) — тот же, что у
# браузерного напоминания.
#
# Состояние игры берётся из транскрипта ВЫЗЫВАЮЩЕГО (lib/transcript.py, caller_transcript): у
# субагента — собственного, иначе Play Mode родителя читался бы как свой. Считаются вызовы,
# МЕНЯЮЩИЕ состояние редактора (`editor-application-set-state`), ОБОИМИ каналами разом: тулом
# MCP-сервера (субагент, которому сервер подключён) и командой оболочки `npx unity-mcp-cli run-tool`
# (главная сессия, у которой тулов этого сервера нет) — канал у одного и того же действия свой на
# каждый запуск, и хук, знающий один, молчал бы у половины вызывающих. Команда опознаётся по
# ПОЗИЦИИ имени в части цепочки (lib/chain_parser.py), не по вхождению его в текст: тем же текстом
# вызов ходит аргументом чужой команды — телом heredoc, шаблоном поиска, фикстурой набора. Игра
# запущена, когда последний такой вызов нёс isPlaying=true. Опрос живого редактора состоянием не
# берётся: он платится сетевым вызовом на КАЖДОЙ правке файла, а цена выше снимаемого промаха.
# Игра, запущенная НЕ вызывающим (человеком, параллельной сессией), в счёт не идёт — останавливать
# чужое канон запрещает.
#
# Повторы: одно напоминание на ЭПИЗОД игры — маркер хранит число вызовов смены состояния на момент
# напоминания, следующее идёт лишь после нового такого вызова. Правка тут НЕ исключение, в отличие
# от браузерного напоминания: серия, в которую уходит вызывающий, у Play Mode состоит как раз из
# правок, и строка на каждой из них дала бы ту самую пачку одинаковых строк подряд; вдобавок первая
# же правка C# роняет игру из Play Mode сама (канон клиента), и повтор утверждал бы состояние, за
# которое хук уже не отвечает. Ключ маркера — идентификатор вызывающего (episode_key): `session_id`
# у субагентов общий с родителем, и один ключ на всех гасил бы напоминание соседям.
# Регистрируется в настройках ОБОИХ проектов абсолютным путём: Play Mode запускают и серверная
# сессия, и работающая из репозитория клиента, а настройки каждая читает свои.
# Не блокирует: инжектит additionalContext.

input=$(cat)
hooks_dir="$(cd "$(dirname "$0")" && pwd)"

{ read -r trigger; read -r episode; read -r key; } < <(python3 -c '
import sys, os, json, re

sys.path.insert(0, os.path.join(sys.argv[1], "lib"))
from chain_parser import command_index, name, split_parts, tokens
from transcript import caller_transcript, episode_key, leaving_step

# Тул смены состояния редактора: MCP-канал зовёт его именем тула, CLI-канал — позиционным
# аргументом команды оболочки.
STATE = "editor-application-set-state"
CLI = "unity-mcp-cli"
# Значение isPlaying в аргументах вызова CLI: между именем поля и значением стоят кавычки,
# двоеточие и экранирование — их набор разнится по форме записи вызова.
VALUE = re.compile(r"isPlaying\W{0,8}(true|false)", re.I)


def truthy(given):
    if isinstance(given, str):
        return given.strip().lower() in ("true", "1", "yes")
    return bool(given)


def cli_parts(command):
    """Части цепочки, ЗАПУСКАЮЩИЕ Unity-CLI: токены части, начиная с имени команды.

    Признак — ПОЗИЦИЯ команды в части (lib/chain_parser.py), не вхождение имени в текст: тем же
    текстом команда ходит аргументом — телом heredoc, фикстурой набора самопроверки, шаблоном
    поиска по транскрипту, — и по вхождению напоминание срывалось бы на собственной работе агента.
    """
    for part in split_parts(command):
        toks = tokens(part)
        start = command_index(toks)
        if start >= len(toks):
            continue

        names = [name(t) for t in toks[start:]]
        if names[0] in ("npx", CLI) and CLI in names:
            yield toks[start:], names


def cli_switch(command):
    """Значение isPlaying у вызова тула смены состояния через CLI; такого вызова нет — None."""
    found = None
    for toks, names in cli_parts(command):
        if "run-tool" not in names or STATE not in names:
            continue

        value = VALUE.search(" ".join(toks))
        if value:
            found = value.group(1).lower() == "true"

    return found


def switches(path):
    """Вызовы смены Play Mode в порядке появления: True — запуск, False — остановка."""
    out = []
    try:
        fh = open(path, "r", errors="ignore")
    except OSError:
        return out

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
                    if "isPlaying" in given:
                        out.append(truthy(given.get("isPlaying")))
                elif called == "Bash":
                    switched = cli_switch(given.get("command") or "")
                    if switched is not None:
                        out.append(switched)

    return out


try:
    d = json.loads(sys.stdin.read())
except Exception:
    sys.exit(0)

trigger = leaving_step(d)
if not trigger:
    sys.exit(0)

# Команда, ЗАПУСКАЮЩАЯ Unity-CLI, — работа с Unity, а не уход от неё.
if d.get("tool_name") == "Bash" and next(cli_parts((d.get("tool_input") or {}).get("command") or ""), None):
    sys.exit(0)

path = caller_transcript(d)
calls = switches(path) if path else []
print(trigger)
print(len(calls) if (calls and calls[-1]) else 0)
print(episode_key(d))
' "$hooks_dir" <<< "$input" 2>/dev/null)

[ -n "${trigger:-}" ] || exit 0
[ "${episode:-0}" -gt 0 ] 2>/dev/null || exit 0

marker="/tmp/unity-playmode-stop-reminder-${key}"
[ "$(cat "$marker" 2>/dev/null)" = "$episode" ] && exit 0
echo "$episode" > "$marker"

case "$trigger" in
  edit)  lead="следующий шаг — правка файла." ;;
  agent) lead="следующий шаг — запуск агента: он работает минутами, игра всё это время висит без присмотра." ;;
  long)  lead="следующий шаг — долгая не-Unity работа (прогон тестов, анализатор, фоновая команда): игра всё это время висит без присмотра." ;;
esac

printf '{"hookSpecificOutput":{"hookEventName":"PreToolUse","additionalContext":"Напоминание: Play Mode запущен твоим вызовом и остановки среди твоих вызовов нет, %s Критерий остановки — канон клиента /mnt/c/Unity/release/CLAUDE.md, «Вход в игру»."}}\n' "$lead"
exit 0
