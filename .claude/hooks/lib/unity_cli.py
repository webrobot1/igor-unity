# Ведомая копия. Источник — /var/www/html/game/.claude/hooks/lib/unity_cli.py в серверном
# репозитории; копию перезаписывает его хук при каждой правке источника. Правка копии теряется
# молча — вносить её в источник. Серверного репозитория нет под рукой → назвать нужную правку
# пользователю.

"""Опознание вызова Unity-CLI (`npx unity-mcp-cli run-tool <тул>`) в команде оболочки — общий
носитель хуков Play Mode. Один и тот же вызов опознают гейт входа в игру (unity-playmode-guard.sh
клиентского репозитория) и напоминание на завершении ответа (unity-playmode-stop-reminder.sh): своя
копия разошлась бы у них МОЛЧА — форма, которую один разбирает, второму невидима, и канал, за
которым гейт следит, второй хук пропускает.
Признак вызова — ПОЗИЦИЯ имени команды в части цепочки (chain_parser), не вхождение имени в текст:
тем же текстом вызов ходит аргументом чужой команды — телом heredoc, поданным на ввод, шаблоном
поиска, фикстурой набора самопроверки.
Канал CLI существует у сессии, которой тулы MCP-сервера не поданы вовсе: тот же тул зовётся тогда
командой оболочки, и условие, накрывающее лишь имя тула, её не видит (skill `shared-gate-mechanics`).
Тот же носитель опознаёт ЗАПУСК самого редактора командой оболочки (`editor_launch`): запустивший
редактор в нём работает, даже не успев позвать ни одного тула, и закрыть его обязан так же.
"""

import re

from shared_chain_parser import command_index, name, split_parts, strip_redirects, tokens

CLI = "unity-mcp-cli"
RUN = "run-tool"

# Исполняемый файл редактора. Запускают его из WSL тремя формами: прямым путём к файлу, командой
# `Start-Process` в строке PowerShell и `start` в строке cmd — в двух последних сам запуск стоит
# в коде, который оболочка Windows исполняет, и разбирается как код этой оболочки.
EDITOR = "unity.exe"
POWERSHELL = ("powershell", "powershell.exe", "pwsh", "pwsh.exe")
CMD = ("cmd", "cmd.exe")
# Слово запуска программы в начале оператора оболочки Windows; `&` — оператор вызова PowerShell.
LAUNCHERS = ("start-process", "saps", "start", "&")
DRIVE = re.compile(r"^([a-z]):(/|$)")


def options(toks):
    """Опции вызова словарём: `--имя значение` и `--имя=значение` одинаково.
    Кавычки снимаются: их снимает и токенизация, но запасной проход chain_parser (команда, которую
    shlex не разобрал) отдаёт токены сырыми."""
    out = {}
    for i, tok in enumerate(toks):
        bare = tok.strip("'\"")
        if not bare.startswith("--"):
            continue
        if "=" in bare:
            key, value = bare.split("=", 1)
            out[key] = value.strip("'\"")
        elif i + 1 < len(toks):
            out[bare] = toks[i + 1].strip("'\"")
    return out


def tool_calls(command, tool):
    """Опции каждого вызова `unity-mcp-cli run-tool <tool>` в команде оболочки; tool None — вызов
    любого тула (работа с редактором вообще, не одна смена его состояния).

    Имя тула у этого канала — позиционный аргумент команды, а не имя вызываемого тула MCP.
    """
    for part in split_parts(command):
        toks = tokens(part)
        start = command_index(toks)
        if start >= len(toks):
            continue

        names = [name(t) for t in toks[start:]]
        if names[0] not in ("npx", CLI) or CLI not in names:
            continue
        if RUN not in names or (tool is not None and tool not in names):
            continue

        yield options(toks[start:])


def _path(text):
    """Путь в единой форме обеих сторон: косая черта, нижний регистр, диск Windows — точкой
    монтирования WSL (`C:\\Unity\\release` и `/mnt/c/Unity/release` — один проект)."""
    p = text.strip("'\"").replace("\\", "/").lower().rstrip("/")
    m = DRIVE.match(p)
    return "/mnt/" + m.group(1) + p[2:] if m else p


def _starts_editor(toks, root):
    """Оператор от его первого слова запускает редактор на проекте `root`: первым стоит сам
    редактор либо слово запуска, среди аргументов — редактор и путь проекта. Значения берутся по
    одному: список аргументов PowerShell (`'-projectPath','C:\\x'`) идёт одним токеном через запятую."""
    if not toks:
        return False
    pieces = [_path(piece) for tok in toks for piece in tok.split(",") if piece]
    if pieces[0].rsplit("/", 1)[-1] != EDITOR and toks[0].strip("'\"").lower() not in LAUNCHERS:
        return False
    return any(p.rsplit("/", 1)[-1] == EDITOR for p in pieces) and root in pieces


def _statements(args, flags):
    """Операторы кода, поданного оболочке Windows после флага из `flags` (регистр не важен), —
    каждый токенами. Один токен за флагом — строка кода, она разбирается на операторы; несколько —
    уже разобранные оболочкой WSL токены одного оператора: склейка их обратно потеряла бы кавычки
    у путей с пробелом."""
    for i, arg in enumerate(args):
        if arg.lower() not in flags or i + 1 >= len(args):
            continue
        rest = args[i + 1:]
        if len(rest) > 1:
            return [rest]
        return [tokens(stmt) for stmt in split_parts(rest[0])]
    return []


def editor_launch(command, project):
    """Команда оболочки запускает редактор Unity на проекте `project`: прямым вызовом его файла
    либо кодом, поданным PowerShell или cmd. Проба процесса (`Get-CimInstance … 'Unity.exe'`),
    листинг каталога редактора и прочее упоминание файла запуском не являются — первым словом
    оператора там стоит чужая команда."""
    root = _path(project)
    for part in split_parts(command):
        toks = strip_redirects(tokens(part))
        start = command_index(toks)
        if start >= len(toks):
            continue

        head = _path(toks[start]).rsplit("/", 1)[-1]
        if head == EDITOR:
            if _starts_editor(toks[start:], root):
                return True
            continue

        if head in POWERSHELL:
            stmts = _statements(toks[start + 1:], ("-command", "-c"))
        elif head in CMD:
            stmts = _statements(toks[start + 1:], ("/c", "/k"))
        else:
            continue

        if any(_starts_editor(stmt, root) for stmt in stmts):
            return True

    return False
