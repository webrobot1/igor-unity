#!/usr/bin/env python3

# Ведомая копия. Источник — /var/www/html/game/.claude/tools/shared-rule-copy.py в серверном
# репозитории; копию перезаписывает его хук при каждой правке источника. Правка копии теряется
# молча — вносить её в источник. Серверного репозитория нет под рукой → назвать нужную правку
# пользователю.

"""Копия свода, которую сессия держала в контексте в момент промаха, — вход разбора причины промаха
(shared-orchestration «Непрерывное обучение», проверка копии правил и модели).

Повод: после сжатия и в чужой сессии загруженной копии в контексте разбирающего нет, а нынешний файл свода
с ней расходится — правками после момента и обрезкой при сжатии.

Пишет файлом: копию свода и её состояние (целиком, обрезана сжатием, выпала при сжатии, в контекст не
приходила), её разницу с нынешним файлом — у добавленного куска правку, которая его внесла (сессия,
исполнитель, время), — модель и уровень рассуждения ответа в момент промаха, число вызовов инструментов и
сжатий до него, последние ответы модели до момента: рассуждение, видимый текст, вызовы инструментов; секреты
проекта в отчёте скрыты. Смысловой оценки не даёт: был ли в копии текст правила, решает сессия чтением файла.

Каналы копии — вызов `Skill`, загрузка своду исполнителю из шапки (`skills:`), импорт `CLAUDE.md`
(вложение инструкций), чтение файла свода, вложение сводов после сжатия. Правка вне сессий программы
(редактор, команда оболочки) в транскриптах не записана — у её куска «источник не найден».

Использование:
  python3 <корень проекта>/.claude/tools/shared-rule-copy.py --session <id> --skill <имя> --out <каталог> [--agent <id>] [--at <uuid либо его начало | время ISO>] [--assistant-steps <число ответов, 10>]
Момент не назван — конец транскрипта; записи с названным идентификатором нет — отказ. Печатает путь файла.
"""
import argparse
import difflib
import glob
import json
import os
import re
import subprocess
import sys

ROOT = os.path.realpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
PROJECTS = os.path.expanduser("~/.claude/projects")
CUT_MARK = "[... skill content truncated for compaction"
SKILL_PATH = re.compile(r"/\.claude/skills/([^/]+)/SKILL\.md$")
READ_LINE = re.compile(r"^\s*\d+\t", re.M)
TIME = re.compile(r"\d{4}-\d{2}")
SECRET_NAME = re.compile(r"PASS|SECRET|TOKEN|KEY|DSN|_URL$|Authorization", re.I)
# Кусок короче этого находится в чужих правках случайно — авторства не доказывает.
MIN_FRAGMENT = 16


def fail(msg):
    print(msg, file=sys.stderr)
    sys.exit(2)


def transcript(session, agent):
    found = glob.glob(os.path.join(PROJECTS, "*", session + ".jsonl"))
    if len(found) != 1:
        fail("транскрипт сессии %s: найдено %d" % (session, len(found)))
    if not agent:
        return found[0]
    path = os.path.join(found[0][:-len(".jsonl")], "subagents", "agent-%s.jsonl" % agent)
    if not os.path.isfile(path):
        fail("транскрипта исполнителя нет: " + path)
    return path


def git(*args):
    out = subprocess.run(["git", "-C", ROOT, *args], capture_output=True, text=True)
    return out.stdout if out.returncode == 0 else ""


def aliases(name):
    """Имена свода по истории: переименование в рабочем дереве и переезды в коммитах."""
    names = {name}
    rel = ".claude/skills/%s/SKILL.md" % name
    paths = {rel}
    # Переименование в индексе и в рабочем дереве; правка поверх переезда снижает сходство, порог понижен.
    renames = git("diff", "--cached", "-M30%", "--name-status", "--", ".claude/skills") \
        + git("diff", "-M30%", "--name-status", "HEAD", "--", ".claude/skills")
    for line in renames.splitlines():
        parts = line.split("\t")
        if parts[0].startswith("R") and len(parts) == 3 and parts[2] == rel:
            paths.add(parts[1])
    for path in list(paths):
        paths.update(p for p in git("log", "--follow", "--name-only", "--format=", "--", path).split() if p)
    for path in paths:
        m = SKILL_PATH.search("/" + path)
        if m:
            names.add(m.group(1))
    return names


def secrets():
    """Ключи и пароли проекта из их носителей: входы вызовов в транскрипте их несут, отчёт их не копирует."""
    found = set()
    for name in (".env", ".env.local"):
        try:
            with open(os.path.join(ROOT, name), errors="ignore") as fh:
                for line in fh:
                    key, eq, value = line.strip().partition("=")
                    if eq and SECRET_NAME.search(key):
                        found.add(value.strip().strip("'\""))
        except OSError:
            pass
    try:
        with open(os.path.join(ROOT, ".mcp.json")) as fh:
            servers = json.load(fh).get("mcpServers") or {}
    except (OSError, ValueError):
        servers = {}
    for server in servers.values():
        found.add(str(server.get("url") or "").rstrip("/").rsplit("/", 1)[-1])
        for key, value in list((server.get("headers") or {}).items()) + list((server.get("env") or {}).items()):
            if SECRET_NAME.search(key):
                found.update(str(value).split())
    # Короткое значение встречается в тексте случайно; длинное заменяется раньше своих кусков.
    return sorted((v for v in found if len(v) >= 8), key=len, reverse=True)


def text_of(content):
    if isinstance(content, str):
        return content
    if isinstance(content, list):
        return "\n\n".join(b.get("text", "") for b in content if isinstance(b, dict) and b.get("type") == "text")
    return ""


def skill_of(path, names):
    m = SKILL_PATH.search(str(path or ""))
    return m and m.group(1) in names


def reached(rec, at):
    """Запись идёт после момента промаха, заданного временем; момент-идентификатор ловит scan."""
    ts = rec.get("timestamp") or ""
    return bool(at) and bool(ts) and bool(TIME.match(at)) and ts[:len(at)] > at


def scan(path, names, at, assistant_steps=10):
    """Копия свода и сведения о моменте: проход по транскрипту до момента включительно."""
    st = {"copy": None, "channel": "в контекст не приходила", "cut": False, "since": "", "model": "", "effort": "",
          "calls": 0, "compactions": 0, "moment": "", "line": 0, "turns": []}
    skill_calls, read_calls = {}, {}
    last = False
    compacted = False
    with open(path, errors="ignore") as fh:
        for n, line in enumerate(fh, 1):
            try:
                rec = json.loads(line)
            except ValueError:
                continue
            if not isinstance(rec, dict):
                continue
            if reached(rec, at) or last:
                break
            last = bool(at) and not TIME.match(at) and str(rec.get("uuid") or "").startswith(at)
            st["found"] = st.get("found") or last
            ts = rec.get("timestamp") or st["moment"]
            st["moment"], st["line"] = ts, n

            def take(text, channel, cut=False):
                st.update(copy=text, channel=channel, cut=cut, since=ts)

            kind = rec.get("type")
            content = (rec.get("message") or {}).get("content")
            att = rec.get("attachment")
            if kind == "system" and rec.get("subtype") == "compact_boundary":
                st["compactions"] += 1
                compacted = True
                if st["copy"] is not None:
                    take(None, "выпала при сжатии")
                continue
            if kind == "attachment" and isinstance(att, dict):
                if att.get("type") == "invoked_skills":
                    for s in att.get("skills") or []:
                        if isinstance(s, dict) and s.get("name") in names:
                            body = s.get("content") or ""
                            take(body, "вложение сводов после сжатия", CUT_MARK in body)
                for f in att.get("files") or []:
                    if isinstance(f, dict) and str(f.get("path") or "").endswith("CLAUDE.md"):
                        st["imports"] = True
                    if isinstance(f, dict) and skill_of(f.get("path"), names):
                        take(f.get("content") or "", "импорт CLAUDE.md")
                if skill_of(att.get("path"), names) and isinstance(att.get("content"), str):
                    take(att["content"], "вложение " + str(att.get("type")))
                continue
            if kind == "assistant" and isinstance(content, list):
                msg = rec.get("message") or {}
                st["model"] = msg.get("model") or st["model"]
                st["effort"] = rec.get("effort") or st["effort"]
                # Ответ модели программа пишет записью на каждый блок, записи одного ответа идут подряд.
                turns, mid = st["turns"], msg.get("id") or rec.get("uuid")
                if not turns or turns[-1]["id"] != mid:
                    turns.append({"id": mid, "uuid": str(rec.get("uuid") or "—"), "timestamp": str(rec.get("timestamp") or "—"),
                                  "compaction": compacted, "thinking": [], "text": [], "tools": []})
                    compacted = False
                turn = turns[-1]
                if len(turns) > assistant_steps:
                    del turns[0]
                for b in content:
                    if not isinstance(b, dict):
                        continue
                    if b.get("type") in ("thinking", "redacted_thinking"):
                        turn["thinking"].append(str(b.get("thinking") or ""))
                        continue
                    if b.get("type") == "text":
                        if b.get("text"):
                            turn["text"].append(str(b["text"]))
                        continue
                    if b.get("type") != "tool_use":
                        continue
                    st["calls"] += 1
                    given = b.get("input") or {}
                    turn["tools"].append((str(b.get("name") or "—"), given))
                    if b.get("name") == "Skill" and str(given.get("skill")) in names:
                        skill_calls[b.get("id")] = True
                    elif b.get("name") == "Read" and skill_of(given.get("file_path"), names):
                        read_calls[b.get("id")] = "offset" in given or "limit" in given
                continue
            if kind != "user":
                continue
            call = rec.get("sourceToolUseID")
            if rec.get("isMeta") and call in skill_calls:
                # Повторный вызов обрезанного свода кладёт перед телом пометку с той же ссылкой — копия
                # вызова — длиннейшее из его сообщений.
                body = text_of(content)
                if st.get("call") != call or len(body) > len(st["copy"] or ""):
                    take(body, "вызов Skill")
                    st["call"] = call
                continue
            if rec.get("isMeta") and isinstance(content, list):
                head = text_of(content[:1])
                m = re.search(r"<command-name>([^<]+)</command-name>", head)
                if m and m.group(1) in names and "<skill-format>" in head:
                    take(text_of(content[1:]), "загрузка из шапки исполнителя")
                continue
            if isinstance(content, list):
                for b in content:
                    if isinstance(b, dict) and b.get("type") == "tool_result" and b.get("tool_use_id") in read_calls:
                        body = READ_LINE.sub("", text_of(b.get("content")) if not isinstance(b.get("content"), str) else b["content"])
                        body = body.split("<system-reminder>")[0]
                        # Окно чтения лежит рядом с прежней копией, не вместо неё.
                        if not read_calls[b["tool_use_id"]]:
                            take(body, "чтение файла свода")
                        elif st["copy"] is None:
                            take(body, "чтение файла свода окном")
    return st


def body_lines(text):
    """Тело свода без служебной строки канала, шапки и пометки обрезки — построчно."""
    text = re.sub(r"^Base directory for this skill: [^\n]*\n+", "", text or "")
    if text.startswith("---\n"):
        end = text.find("\n---", 4)
        if end >= 0:
            text = text[end + 4:]
    text = text.split(CUT_MARK)[0]
    return [ln for ln in text.strip("\n").split("\n")]


def edits(names):
    """Правки файла свода под любым его именем во всех транскриптах всех проектов — общий свод правят и
    соседи по синхронизации: время, сессия, исполнитель, было, стало."""
    suffixes = tuple("/.claude/skills/%s/SKILL.md" % n for n in names)
    files = subprocess.run(["grep", "-rlF", "--include=*.jsonl", "SKILL.md", PROJECTS], capture_output=True, text=True).stdout.split()
    out = []
    for path in files:
        session = os.path.basename(path)[:-6]
        who = "главная сессия"
        if "/subagents/" in path:
            session = path.split(os.sep)[-3]
            try:
                with open(path[:-6] + ".meta.json") as fh:
                    who = "исполнитель " + str(json.load(fh).get("agentType"))
            except (OSError, ValueError):
                who = "исполнитель"
        with open(path, errors="ignore") as fh:
            for line in fh:
                if "SKILL.md" not in line or '"tool_use"' not in line:
                    continue
                try:
                    rec = json.loads(line)
                except ValueError:
                    continue
                content = (rec.get("message") or {}).get("content") if isinstance(rec, dict) else None
                if not isinstance(content, list):
                    continue
                for b in content:
                    if not isinstance(b, dict) or b.get("type") != "tool_use" or b.get("name") not in ("Edit", "Write", "MultiEdit"):
                        continue
                    given = b.get("input") or {}
                    if not str(given.get("file_path", "")).endswith(suffixes):
                        continue
                    pairs = given.get("edits") or [given]
                    for p in pairs:
                        old = p.get("old_string") or ""
                        new = p.get("new_string") if "new_string" in p else p.get("content") or ""
                        out.append((rec.get("timestamp") or "", session, who, b.get("name"), old, new or ""))
    out.sort()
    return out


def source(fragment, changes):
    """Правки, внёсшие кусок: первая, у которой он есть в «стало» и нет в «было». Кусок, собранный
    несколькими правками, делится на предложения, затем на части предложения."""
    for ts, session, who, tool, old, new in changes:
        if fragment in new and fragment not in old:
            return ["%s, сессия %s, %s, %s" % (ts, session, who, tool)]
    for splitter in (r"(?<=[.!?»])\s+", r"(?<=[;:,—])\s+"):
        parts = [p.strip() for p in re.split(splitter, fragment) if len(p.strip()) >= MIN_FRAGMENT]
        if len(parts) > 1:
            found = []
            for part in parts:
                got = source_one(part, changes)
                if got and got not in found:
                    found.append(got)
            if found:
                return found
    return ["источник не найден"]


def source_one(part, changes):
    for ts, session, who, tool, old, new in changes:
        if part in new and part not in old:
            return "%s, сессия %s, %s, %s" % (ts, session, who, tool)
    return ""


def fragments(old, new):
    """Куски нового текста, которых в старом нет, — целыми словами, не короче порога."""
    spans = [m.span() for m in re.finditer(r"\S+", new)]
    sm = difflib.SequenceMatcher(None, re.findall(r"\S+", old), [new[a:b] for a, b in spans], autojunk=False)
    out = []
    for op, _i1, _i2, j1, j2 in sm.get_opcodes():
        if op in ("insert", "replace"):
            frag = new[spans[j1][0]:spans[j2 - 1][1]]
            if len(frag) >= MIN_FRAGMENT:
                out.append(frag)
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--session", required=True)
    ap.add_argument("--skill", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--agent", default="")
    ap.add_argument("--at", default="")
    ap.add_argument("--assistant-steps", type=int, default=10,
                    help="сколько последних ответов модели до момента показать в отчёте; 0 — раздел не пишется")
    a = ap.parse_args()
    if a.assistant_steps < 0:
        fail("--assistant-steps должен быть >= 0")

    current = os.path.join(ROOT, ".claude", "skills", a.skill, "SKILL.md")
    if not os.path.isfile(current):
        fail("файла свода нет: " + current)
    if not os.path.isdir(a.out):
        fail("каталога вывода нет: " + a.out)

    names = aliases(a.skill)
    path = transcript(a.session, a.agent)
    st = scan(path, names, a.at, a.assistant_steps)
    if a.at and not TIME.match(a.at) and not st.get("found"):
        fail("записи %s в транскрипте нет: момент — время ISO либо идентификатор записи (uuid, можно началом)" % a.at)

    with open(current, errors="ignore") as fh:
        cur = body_lines(fh.read())
    copy = body_lines(st["copy"]) if st["copy"] is not None else []
    state = ("обрезана сжатием" if st["cut"] else "целиком") if st["copy"] is not None else "свода в контексте нет"
    # Импорт CLAUDE.md программа пишет в транскрипт не во всех версиях (до 2.1.261 — через раз): без его
    # записи копия, пришедшая импортом, не видна, и отсутствие копии не доказано.
    if st["copy"] is None and st["channel"] == "в контекст не приходила" and not st.get("imports"):
        state = "не видно: записи импорта CLAUDE.md в транскрипте нет — пришёл ли свод импортом, решает CLAUDE.md сессии"

    report = []
    turns = st["turns"]
    assistant_context = []
    if a.assistant_steps:
        assistant_context += [
            "## Ответы модели перед моментом",
            "",
            "- Последние %d ответов модели до момента включительно (запрошено %d); результаты инструментов и реплики "
            "пользователя не показаны. Уровень рассуждения — в шапке отчёта." % (len(turns), a.assistant_steps),
            "",
        ]
        for i, turn in enumerate(turns, 1):
            assistant_context += ["### Ответ %d" % i, "", "- uuid первой записи: %s" % turn["uuid"],
                                  "- время: %s" % turn["timestamp"]]
            if turn["compaction"]:
                assistant_context.append("- первый ответ после сжатия")
            assistant_context.append("")
            thinking = [t for t in turn["thinking"] if t]
            if thinking:
                assistant_context += ["Рассуждение:", "", "\n\n".join(thinking), ""]
            elif turn["thinking"]:
                assistant_context += ["(рассуждение было, его текст в транскрипт не записан)", ""]
            if turn["text"]:
                assistant_context += ["Видимый текст:", "", "\n\n".join(turn["text"]), ""]
            if turn["tools"]:
                assistant_context.append("Вызовы инструментов:")
                for name, given in turn["tools"]:
                    assistant_context += ["- `%s`" % name, "```json", json.dumps(given, ensure_ascii=False, indent=2), "```"]
                assistant_context.append("")
        if not turns:
            assistant_context += ["- ответов модели до момента нет", ""]
    if st["copy"] is not None:
        changes = edits(names)
        # Обрезка режет строку посередине: недорезанная строка в сравнение не идёт, хвост файла за ней —
        # отрезанное сжатием.
        whole = copy[:-1] if st["cut"] else copy
        tail = len(cur)
        if st["cut"] and copy and copy[-1].strip():
            starts = [k for k, ln in enumerate(cur) if ln.startswith(copy[-1][:60])]
            if starts:
                tail = starts[-1]
        sm = difflib.SequenceMatcher(None, whole, cur[:tail], autojunk=False)
        for op, i1, i2, j1, j2 in sm.get_opcodes():
            if op == "equal":
                continue
            if op == "delete":
                report.append("- снято после момента:\n  > " + "\n  > ".join(whole[i1:i2]))
                continue
            new = "\n".join(cur[j1:j2])
            if st["cut"] and i1 >= len(whole):
                report.append("- в копии отрезано сжатием: строки %d–%d нынешнего файла, начиная с «%s»"
                              % (j1 + 1, j2, cur[j1][:120]))
                continue
            for frag in fragments("\n".join(whole[i1:i2]), new):
                report.append("- добавлено: «%s»\n  внёс: %s" % (frag if len(frag) <= 300 else frag[:300] + " …",
                                                               "; ".join(source(frag, changes))))
        if tail < len(cur):
            report.append("- в копии отрезано сжатием: строки %d–%d нынешнего файла, начиная с «%s»"
                          % (tail + 1, len(cur), cur[tail][:120]))

    who = "исполнитель %s" % a.agent if a.agent else "главная сессия"
    lines = [
        "# Копия свода %s в момент промаха" % a.skill,
        "",
        "- транскрипт: %s (%s), запись %d, время %s" % (path, who, st["line"], st["moment"]),
        "- модель ответа: %s, уровень рассуждения: %s" % (st["model"] or "не записана", st["effort"] or "не записан"),
        "- вызовов инструментов до момента: %d, сжатий до момента: %d" % (st["calls"], st["compactions"]),
        "- копия: %s; канал — %s; пришла %s" % (state, st["channel"], st["since"] or "—"),
        "- имена свода по истории: %s" % ", ".join(sorted(names)),
        "",
        *assistant_context,
        "## Разница копии с нынешним файлом",
        "",
        *(report or ["- расхождений нет" if st["copy"] is not None else "- сравнивать не с чем"]),
    ]
    if st["copy"] is not None:
        lines += ["", "## Копия", "", *copy]

    dest = os.path.join(a.out, "rule-copy-%s-%s%s.md" % (a.skill, a.session[:8], "-" + a.agent[:8] if a.agent else ""))
    text = "\n".join(lines) + "\n"
    for secret in secrets():
        text = text.replace(secret, "<секрет>")
    with open(dest, "w") as fh:
        fh.write(text)
    print(dest)


if __name__ == "__main__":
    main()
