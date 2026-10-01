#!/bin/bash

# Ведомая копия. Источник — /var/www/html/game/.claude/hooks/unity-question-skill-guard.sh в
# серверном репозитории; копию перезаписывает его хук при каждой правке источника. Правка копии
# теряется молча — вносить её в источник. Серверного репозитория нет под рукой → назвать нужную
# правку пользователю.

# PreToolUse hook (AskUserQuestion): окно вопроса о Unity отбивается, пока свод `unity` не загружен.
# Повод — промах сессии: вопрос о редакторе ушёл человеку без свода, и человеку досталось решение,
# которое свод отдаёт агенту (редактор запускает и закрывает агент сам — `unity` «Запуск и
# закрытие редактора»). Правило-владелец — `shared-core` «Профильный skill перед действием в
# домене»: диалог с пользователем — действие в домене; хук садится в момент решения.
# Отказ, а не напоминание: напоминание на `PreToolUse` окна доезжает до модели лишь после ответа
# человека — вопрос уже ушёл без свода (решение пользователя 2026-09-24, окно «Хук Unity» →
# «Отбивать окно (рекомендация)»).
# Признак вопроса о Unity — слова «Unity» либо «редактор» в любой строке окна, включая подписи
# вариантов. «Редактор» бывает и кода, и карты, и Tiled: ложный отказ стоит одной загрузки свода
# и повтора окна — цену принял пользователь тем же решением.
# Загруженным считается тело свода в контексте после последнего сжатия (`lib/shared_transcript.py`
# skills_loaded). Транскрипт вызывающего не найден — загрузка не наблюдаема, окно проходит.
# Маркера между вызовами нет: загрузку проверяет транскрипт, повтор окна после загрузки проходит сам.
# Отказ отдаётся обоими каналами разом (skill `shared-gate-mechanics`): JSON permissionDecision и
# код 2 со stderr. Разбор сорвался (вход не JSON, нет python3) → молчит.
# Текст отказа — осознанный дубль пометки в своде `unity` «Запуск и закрытие редактора», правится
# парно. Набор самопроверки — unity-question-skill-guard.test.sh.

input=$(cat)
hooks_dir="$(cd "$(dirname "$0")" 2>/dev/null && pwd)"

command -v python3 >/dev/null 2>&1 || exit 0

err=$(mktemp 2>/dev/null) || err=/dev/null
out=$(HOOKS_DIR="$hooks_dir" python3 -c '
import json, os, re, sys

sys.path.insert(0, os.path.join(os.environ["HOOKS_DIR"], "lib"))
from shared_transcript import caller_transcript, skills_loaded

SKILL = "unity"
# Слово «Unity» целиком и любая форма «редактор…».
WORDS = re.compile(r"\bunity\b|редактор", re.IGNORECASE)

REASON = ("Окно отбито: вопрос называет Unity либо редактор, а свод `unity` в контексте не загружен. "
          "Загрузить Skill(\"unity\") и сверить вопрос с ним: редактор клиента запускает и закрывает "
          "агент сам («Запуск и закрытие редактора»), такое решение человеку не выносится. Нужен "
          "вопрос и после свода — задать окно заново.")


def strings(node, out):
    if isinstance(node, str):
        out.append(node)
    elif isinstance(node, dict):
        for v in node.values():
            strings(v, out)
    elif isinstance(node, list):
        for v in node:
            strings(v, out)


try:
    d = json.loads(sys.stdin.read())
except ValueError:
    sys.exit(0)

buf = []
strings(d.get("tool_input") or {}, buf)
if not WORDS.search(" ".join(buf)):
    sys.exit(0)

path = caller_transcript(d)
loaded = skills_loaded(path) if path else None
if loaded is None or SKILL in loaded:
    sys.exit(0)

print(json.dumps({"hookSpecificOutput": {"hookEventName": "PreToolUse",
                                         "permissionDecision": "deny",
                                         "permissionDecisionReason": REASON}},
                 ensure_ascii=False))
print(REASON, file=sys.stderr)
sys.exit(2)
' <<< "$input" 2>"$err")
rc=$?

[ -n "$out" ] && printf '%s\n' "$out"
if [ "$rc" = 2 ]; then
    cat "$err" >&2
    [ "$err" != /dev/null ] && rm -f "$err"
    exit 2
fi
[ "$err" != /dev/null ] && rm -f "$err"
exit 0
