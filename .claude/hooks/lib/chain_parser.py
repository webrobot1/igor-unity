# Ведомая копия. Источник — /var/www/html/game/.claude/hooks/lib/chain_parser.py в серверном
# репозитории; копию перезаписывает его хук при каждой правке источника. Правка копии теряется
# молча — вносить её в источник. Серверного репозитория нет под рукой → назвать нужную правку
# пользователю.

"""Разбор shell-цепочки команд — общий носитель командных гейтов каталога: разрезают команду на
части, токенизируют часть, находят в части позицию имени команды, берут имя из токена.
Каждый хук импортирует отсюда (sys.path.insert на свой каталог + `from chain_parser import ...`),
вместо вручную синхронизируемых копий у каждого. Расхождение с этим источником (собственное
определение вместо импорта) ловит .claude/hooks/command-guard.test.sh — там же перечень хуков,
обязанных импортировать."""

import re
import shlex

CHAIN = ';|&\n()'
ASSIGN = re.compile(r'^[A-Za-z_][A-Za-z0-9_]*=')
SHELLS = {'sh', 'bash', 'zsh', 'dash', 'ksh'}
# Опции sudo/doas, забирающие следующий токен значением: пропустить оба, иначе значение
# прочлось бы командой.
SUDO_VALUE_OPTS = {'-u', '-g', '-p', '-C', '-U', '-r', '-t', '-h', '--user', '--group', '--prompt'}
# Ключевые слова оболочки и обёртки без собственных опций-значений. Часть цепочки после разреза
# начинается с них (`do rm …`, `then git reset …`, `nohup rm …`), и без пропуска именем команды
# прочлось бы само ключевое слово: гейт молчит на вызове, записанном телом цикла либо ветвью
# условия, — тем же вызове, ради которого он заведён.
LEADING = {'do', 'then', 'else', 'elif', '!', '{', 'if', 'while', 'until', 'time', 'command',
           'builtin', 'exec', 'nohup', 'nice'}
# Обёртки, за которыми стоит НАСТОЯЩАЯ команда, и опции каждой, забирающие следующий токен
# значением: без пропуска значения оно прочлось бы командой.
WRAPPER_VALUE_OPTS = {
    'sudo': SUDO_VALUE_OPTS,
    'doas': SUDO_VALUE_OPTS,
    # `env -u VAR cmd` снимает переменную окружения перед запуском: без пропуска ЗНАЧЕНИЯ именем
    # команды читается имя переменной, и гейт молчит на вызове под обёрткой — ровно на той форме,
    # которой снимают переменную-защиту перед запуском.
    'env': {'-u', '--unset', '-C', '--chdir'},
    'xargs': {'-n', '-P', '-I', '-i', '-s', '-d', '-E', '-L', '--max-args', '--max-procs',
              '--replace', '--delimiter', '--max-lines'},
    # Обёртка запуска команд проекта (`bin/run <команда>`): выбирает контур исполнения и зовёт
    # переданное как есть. Своих опций у неё нет — команда стоит первым же токеном за ней.
    # Без пропуска обёртки именем команды читается она сама, и КАЖДЫЙ гейт этого каталога молчит
    # на вызове под ней — ровно на той форме, которой команды проекта и запускаются.
    'run': set(),
}


def split_parts(command):
    """Разрез цепочки на команды, ЧТУЩИЙ кавычки: разделитель внутри строки — данные, не граница.
    Иначе команда, несущая чужой текст аргументом (`sed -i "s|A|B|"`, `grep -E "a|b"`, сборка ТЗ,
    тест-набор), прочлась бы вызовом, которым не является. Части возвращаются стрипнутыми,
    пустые отфильтрованы."""
    parts, buf, quote, i, n = [], [], None, 0, len(command)
    while i < n:
        ch = command[i]
        if quote:
            buf.append(ch)
            if ch == '\\' and quote == '"' and i + 1 < n:
                buf.append(command[i + 1])
                i += 2
                continue
            if ch == quote:
                quote = None
            i += 1
            continue
        if ch == '\\' and i + 1 < n:
            buf.append(ch)
            buf.append(command[i + 1])
            i += 2
            continue
        if ch in '"\'':
            quote = ch
            buf.append(ch)
            i += 1
            continue
        if ch in CHAIN:
            parts.append(''.join(buf))
            buf = []
            i += 1
            continue
        buf.append(ch)
        i += 1
    parts.append(''.join(buf))
    return [p for p in (part.strip() for part in parts) if p]


def tokens(part):
    try:
        return shlex.split(part)
    except ValueError:
        return part.split()


def name(token):
    return token.lstrip('\\').rsplit('/', 1)[-1]


def command_index(toks, wrappers=WRAPPER_VALUE_OPTS):
    """Индекс токена с ИМЕНЕМ команды в части цепочки: перед ним стоят присваивания, ключевые
    слова оболочки (LEADING) и обёртки со своими опциями-значениями. len(toks) — команды в части
    нет. Хвост разреза цепочки (`/путь/x.md)` из `$(… /путь/x.md)`) с имени снимается: часть
    токенизируют и в обход split_parts."""
    i = 0
    while i < len(toks):
        t = toks[i]
        if ASSIGN.match(t) or t in LEADING:
            i += 1
            continue
        wrapper = wrappers.get(name(t.strip('"\'').strip('();,')))
        if wrapper is None:
            break
        i += 1
        while i < len(toks):
            x = toks[i]
            if ASSIGN.match(x):
                i += 1
            elif x.startswith('-'):
                i += 2 if x in wrapper else 1
            else:
                break
    return i
