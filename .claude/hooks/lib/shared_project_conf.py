# Ведомая копия. Источник — /var/www/html/game/.claude/hooks/lib/shared_project_conf.py в
# серверном репозитории; копию перезаписывает его хук при каждой правке источника. Правка копии
# теряется молча — вносить её в источник. Серверного репозитория нет под рукой → назвать нужную
# правку пользователю.

"""Проектные значения общих хуков — `.claude/project.conf` (формат — шапка самого файла) — для
Python-модулей каталога lib: разборщик вызова стоит под всеми командными гейтами, и передача значения
окружением через bash-часть каждого из них разошлась бы молча. Файл ищется двумя уровнями выше каталога
lib; переменная окружения CLAUDE_PROJECT_CONF подменяет путь — ею наборы самопроверки подают свою
фикстуру. Нет файла либо ключа — пустое значение, как у bash-части: проект, не объявивший значение
(ведомые копии у внешних контуров файла не несут), получает поведение без проектной особенности."""

import os
import shlex

_DEFAULT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'project.conf')
_cache = {}


def _load(path):
    values = {}
    try:
        with open(path, encoding='utf-8') as f:
            lines = f.read().splitlines()
    except OSError:
        return values
    for line in lines:
        line = line.strip()
        if not line or line.startswith('#') or '=' not in line:
            continue
        key, raw = line.split('=', 1)
        try:
            parts = shlex.split(raw)
        except ValueError:
            continue
        values[key.strip()] = parts[0] if parts else ''
    return values


def _path():
    return os.environ.get('CLAUDE_PROJECT_CONF') or _DEFAULT


def value(key):
    """Значение ключа строкой; нет файла либо ключа — пустая строка."""
    path = _path()
    if path not in _cache:
        _cache[path] = _load(path)
    return _cache[path].get(key, '')


def root():
    """Корень проекта, чьи значения читаются: файл лежит в его `.claude`."""
    return os.path.realpath(os.path.join(os.path.dirname(_path()), '..'))


def words(key):
    """Значение-перечень (через пробел) списком."""
    return value(key).split()


def words_at(proj, key):
    """Значение-перечень ключа из `.claude/project.conf` другого корня зоны проекта (каталог
    `permissions.additionalDirectories`, его репозиторий): у него свои значения. Нет файла либо ключа —
    пустой перечень."""
    path = os.path.join(proj, '.claude', 'project.conf')
    if path not in _cache:
        _cache[path] = _load(path)
    return _cache[path].get(key, '').split()
