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
командой оболочки, и условие, накрывающее лишь имя тула, её не видит (skill `gate-mechanics`).
"""

from chain_parser import command_index, name, split_parts, tokens

CLI = "unity-mcp-cli"
RUN = "run-tool"


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
    """Опции каждого вызова `unity-mcp-cli run-tool <tool>` в команде оболочки.

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
        if RUN not in names or tool not in names:
            continue

        yield options(toks[start:])
