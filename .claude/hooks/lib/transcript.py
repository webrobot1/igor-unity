# Ведомая копия. Источник — /var/www/html/game/.claude/hooks/lib/transcript.py в серверном
# репозитории; копию перезаписывает его хук при каждой правке источника. Правка копии теряется
# молча — вносить её в источник. Серверного репозитория нет под рукой → назвать нужную правку
# пользователю.

"""Разбор события хука: разрешение транскриптов, состояние браузерного эпизода вызывающего и
ярлык шага, уводящего его от инструмента.

Общий носитель хуков: браузерные (`playwright-close-reminder.sh`,
`playwright-artifacts-clean.sh`) спрашивают, открыт ли браузер У ВЫЗВАВШЕГО; сессионные
(`platform-spec-reminder.sh`, `plan-hygiene-reminder.sh`) — что делала ВСЯ сессия, включая
своих субагентов (`session_transcripts`, измерения — `lib/session_scope.py`). Копии
резолвера разошлись бы молча.

Момент ухода (`leaving_step`) делят напоминания об ОСТАВЛЕННОМ ресурсе — браузере и Play Mode
Unity: ярлык шага у них один, различается лишь ресурс и его состояние.

Раскладка транскриптов: у главной сессии — `<projects>/<slug>/<session_id>.jsonl`, у субагента
любой глубины — `<projects>/<slug>/<session_id>/subagents/agent-<agent_id>.jsonl`, где
`session_id` КОРНЕВОЙ сессии. Что программа кладёт в `transcript_path` субагенту, её докой не
объявлено, — оттого путь берётся не из поля, а собирается по `session_id` и `agent_id`, а поле
принимается лишь когда само указывает на файл субагента.
Своего транскрипта не нашлось — пусто: транскрипт РОДИТЕЛЯ за свой не выдавать, чужой браузерный
эпизод в нём читался бы как собственный.
"""
import json
import os
import re

BROWSER = "mcp__plugin_playwright_playwright__browser_"
CLOSE = BROWSER + "close"

# Каталог артефактов браузера: имя знает домен, а не место вызова — хук строит по нему путь,
# разбор транскрипта отделяет им адрес артефакта от прочего текста.
ARTIFACT_DIR = ".playwright-mcp"

# Матчит только tool_use-блоки assistant-строк: в attachment/tool_result имя тула экранировано
# и в таком сыром виде не встречается.
_CALL = re.compile(r'"name":"(%s[a-z_]+)"' % BROWSER)

# Адрес внутри каталога артефактов в любой форме записи — относительной от корня проекта и
# абсолютной. Хвост режется по разделителям текста и по `#`: ссылка на строку файла (`…log#L1`)
# частью имени не является.
_ARTIFACT = re.compile(r"[^\s\"'()\[\]]*%s/[^\s\"'()\[\]#|]+" % re.escape(ARTIFACT_DIR))


def caller_transcript(event):
    """Путь к транскрипту вызывающего либо пустая строка."""
    given = event.get("transcript_path") or ""
    agent_id = event.get("agent_id") or ""

    if not agent_id:
        # Тип агента без его идентификатора: собственный транскрипт не адресовать, а `given`
        # тогда чужой — молчим.
        if event.get("agent_type"):
            return ""

        return given if given and os.path.isfile(given) else ""

    leaf = "agent-%s.jsonl" % agent_id
    if os.path.basename(given) == leaf and os.path.isfile(given):
        return given

    session = event.get("session_id") or ""
    if given and session:
        cand = os.path.join(os.path.dirname(given), session, "subagents", leaf)
        if os.path.isfile(cand):
            return cand

    return ""


def browser_calls(path):
    """Имена браузерных вызовов в порядке появления."""
    out = []
    try:
        with open(path, "r", errors="ignore") as fh:
            for line in fh:
                out.extend(_CALL.findall(line))
    except OSError:
        return []

    return out


def browser_artifacts(path):
    """Пути артефактов, порождённых браузерной работой вызывающего, — относительно каталога
    артефактов, в порядке появления.

    Авторство несёт САМ браузерный вызов: имя файла в его параметрах и адреса в его выводе
    (снимок страницы, журнал консоли, кадр). Прочие вхождения того же каталога в транскрипт
    авторства не несут вовсе — команда оболочки над чужим файлом, чтение чужого содержимого,
    материалы соседней работы, положенные туда же, — оттого вывод берётся привязанным к
    идентификатору браузерного вызова, не поиском по строке транскрипта.
    """
    ids = set()
    out = []
    try:
        fh = open(path, "r", errors="ignore")
    except OSError:
        return out

    with fh:
        for line in fh:
            # Имя тула стоит только в строке самого вызова: в строке его вывода программа
            # оставляет один идентификатор, и по имени такая строка не находится.
            if BROWSER not in line and ARTIFACT_DIR not in line:
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
                if not isinstance(block, dict):
                    continue

                kind = block.get("type")
                if kind == "tool_use" and str(block.get("name") or "").startswith(BROWSER):
                    if block.get("id"):
                        ids.add(block["id"])

                    given = block.get("input")
                    given = given.get("filename") if isinstance(given, dict) else None
                    text = given if isinstance(given, str) else ""
                elif kind == "tool_result" and block.get("tool_use_id") in ids:
                    body = block.get("content")
                    text = body if isinstance(body, str) else json.dumps(body, ensure_ascii=False)
                else:
                    continue

                for found in _ARTIFACT.findall(text):
                    rel = found.split(ARTIFACT_DIR + "/", 1)[1]
                    # Адрес каталога артефактом не является: он общий, автора у него нет.
                    if rel and not rel.endswith("/") and rel not in out:
                        out.append(rel)

    return out


def browser_open(calls):
    """Браузер открыт: вызовы были, последний — не закрытие."""
    return bool(calls) and calls[-1] != CLOSE


def episode_key(event):
    """Ключ, разделяющий эпизоды разных вызывающих: у субагентов `session_id` общий с родителем."""
    return event.get("agent_id") or event.get("session_id") or "default"


def session_transcripts(event):
    """Транскрипты сессии: свой плюс субагентские. Работа субагента — работа сессии, его
    правки идут в её же рабочее дерево. Порядок: свой первым."""
    out = []
    own = caller_transcript(event)
    if own:
        out.append(own)

    session = event.get("session_id") or ""
    given = event.get("transcript_path") or ""
    if not session or not given:
        return out

    root = os.path.dirname(given)
    # Каталог субагентов адресуется КОРНЕВОЙ сессией: у субагента `session_id` общий с родителем,
    # оттого один и тот же каталог виден и главной сессии, и её исполнителям.
    subdir = os.path.join(root, session, "subagents")
    try:
        names = sorted(os.listdir(subdir))
    except OSError:
        return out

    for name in names:
        if not name.endswith(".jsonl"):
            continue
        path = os.path.join(subdir, name)
        if path not in out and os.path.isfile(path):
            out.append(path)

    return out


# Дефолт таймаута Bash-тула: больший выставляют, только ожидая долгого прогона.
_BASH_TIMEOUT = 120000

# Раннеры добирают частый случай, где длительность не объявляют; границей срабатывания перечень
# не является — иначе напоминание приходило бы на каждую команду оболочки и стало бы фоновым шумом.
_RUNNER = re.compile(r"phpunit|phpstan|psalm|rector|php-cs-fixer"
                     r"|composer\s+(install|update|require)"
                     r"|npm\s+(ci|install|run|test)")


def leaving_step(event):
    """Ярлык шага, на котором вызывающий уходит от инструмента, которым только что работал:
    `edit` — правка файла, `agent` — запуск агента (работает минутами), `long` — долгая работа
    (прогон тестов, анализатор, фоновая команда). Шаг не уводит — пустая строка.

    Долгота команды берётся из ДЕКЛАРАЦИИ самого вызова (`run_in_background`, `timeout` сверх
    дефолтного): она не устаревает и не зависит от имён инструментов.
    Какой ресурс оставлен и в каком он состоянии, решает сам хук — тут только момент ухода;
    шаг, уводящий от одного инструмента, для другого бывает работой (вызов Unity-CLI командой
    оболочки), и такое исключение остаётся за вызывающим хуком.
    """
    tool = event.get("tool_name") or ""
    if tool in ("Edit", "Write"):
        return "edit"

    if tool in ("Agent", "Task"):
        return "agent"

    if tool.endswith("__tests-run"):
        return "long"

    if tool != "Bash":
        return ""

    given = event.get("tool_input") or {}
    try:
        timeout = int(given.get("timeout") or 0)
    except (TypeError, ValueError):
        timeout = 0

    if given.get("run_in_background") is True or timeout > _BASH_TIMEOUT:
        return "long"

    return "long" if _RUNNER.search(given.get("command", "") or "") else ""
