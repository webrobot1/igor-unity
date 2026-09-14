# Ведомая копия. Источник — /var/www/html/game/.claude/hooks/lib/project_scope.py в серверном
# репозитории; копию перезаписывает его хук при каждой правке источника. Правка копии теряется
# молча — вносить её в источник. Серверного репозитория нет под рукой → назвать нужную правку
# пользователю.

"""Каталоги проекта — граница зоны у гейтов, которым она нужна: корень проекта, каталоги
`permissions.additionalDirectories` из `.claude/settings.json` проекта вместе с репозиториями, которым
они принадлежат, и реальные каталоги за симлинками бандлов узла (`Build/*`) и каталога артефактов
`.playwright-mcp`.

Носитель один на гейты: запись вне проекта отбивает write-scope-guard, снятие рабочего состояния
репозитория внутри проекта — git-state-guard. Копия множества во втором хуке разошлась бы с первой
молча. Корень передаёт хук: у копии хука в другом репозитории это корень того репозитория.
"""
import json
import os

SETTINGS = ("settings.json",)


def settings_dirs(proj):
    """`permissions.additionalDirectories` из настроек проекта."""
    dirs = []
    for fname in SETTINGS:
        try:
            with open(os.path.join(proj, ".claude", fname), encoding="utf-8") as fh:
                data = json.load(fh)
        except (OSError, ValueError):
            continue
        for d in ((data.get("permissions") or {}).get("additionalDirectories") or []):
            if isinstance(d, str) and d:
                dirs.append(os.path.normpath(os.path.expanduser(d)))
    return dirs


def repo_of(path):
    """Корень репозитория, которому принадлежит путь: ближайший каталог вверх с `.git` (каталог
    либо файл у рабочего дерева). None — не в репозитории либо каталог не смонтирован."""
    cur = path
    while True:
        if os.path.exists(os.path.join(cur, ".git")):
            return cur
        up = os.path.dirname(cur)
        if up == cur:
            return None
        cur = up


def links(proj):
    """Реальные каталоги за симлинками бандлов узла и каталога артефактов."""
    res = []
    build = os.path.join(proj, "Build")
    try:
        names = [os.path.join(build, n) for n in os.listdir(build)]
    except OSError:
        names = []
    names.append(os.path.join(proj, ".playwright-mcp"))
    for p in names:
        if os.path.islink(p):
            res.append(os.path.realpath(p))
    return res


def inside(path, roots):
    return any(path == r or path.startswith(r + "/") for r in roots)


def project_roots(proj):
    """Корни проекта: сам корень, объявленные каталоги с их репозиториями, реальные каталоги за
    симлинками. Повторы и вложенные каталоги не чистятся — перечень для показа чистит distinct."""
    res = [proj]
    for d in settings_dirs(proj):
        res.append(d)
        top = repo_of(d)
        if top:
            res.append(top)
    return res + links(proj)


def distinct(roots):
    """Перечень без повторов и без каталогов, лежащих внутри другого из перечня: границу они не меняют."""
    res = []
    for r in roots:
        r = r.rstrip("/") or "/"
        if r not in res:
            res.append(r)
    return [r for r in res if not inside(r, [o for o in res if o != r])]
