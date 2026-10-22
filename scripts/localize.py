#!/usr/bin/env python3
"""Localization helper for Adawat's Swift sources.

  localize.py wrap      Wraps the first string literal of UI calls (Text, Button, Toggle, help…) in L10n.t(…).
  localize.py extract   Lists every user-facing English string as JSON: {"static": [...], "dynamic": [...]}.

Dynamic strings contain Swift interpolations; they become regex templates in Patterns.strings.
"""
import json
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent / "Sources" / "Adawat"

# Calls whose first argument is visible text.
CALLS = [
    "Text(", "Button(", "Toggle(", "Label(", ".help(", "ToolRow(title: ", "ToolScaffold(title: ", "Picker(",
    "DisclosureGroup(", "TextField(", "ColorPicker(", "Section(", "LabeledContent(", "NSMenuItem(title: ",
    "ToolWindows.present(", "title: ", "panel.message = ", "panel.prompt = ", "HUDController.shared.show(",
]


def literal_end(source, start):
    """Index just past the string literal starting at `start` (a quote), honoring \\( … ) nesting."""
    i = start + 1
    while i < len(source):
        c = source[i]
        if c == "\\":
            if source[i + 1] == "(":
                depth, i = 1, i + 2
                while depth:
                    if source[i] == '"':
                        i = literal_end(source, i)
                        continue
                    depth += {"(": 1, ")": -1}.get(source[i], 0)
                    i += 1
                continue
            i += 2
            continue
        if c == '"':
            return i + 1
        i += 1
    raise ValueError("unterminated literal")


def literals(source):
    """Every single-line string literal with its span."""
    i = 0
    while i < len(source):
        # Multi-line strings open with """ and a line break; skip their body.
        if source.startswith('"""', i) and source[i + 3:i + 4] == "\n":
            i = source.index('"""', i + 3) + 3
            continue
        # Raw strings: #"…"#
        if source.startswith('#"', i):
            i = source.index('"#', i + 2) + 2
            continue
        if source[i] == '"':
            end = literal_end(source, i)
            yield i, end, source[i + 1:end - 1]
            i = end
            continue
        if source.startswith("//", i):
            i = source.index("\n", i)
            continue
        i += 1


def looks_like_text(value):
    if not re.search(r"[A-Za-z]", value) or len(value) < 2:
        return False
    if value.startswith(("-", "/", ".", "<", "#", "%", "@", "com.", "public.", "org.", "http", "urn:", "x-")):
        return False
    if re.fullmatch(r"[a-z0-9_.:\-]+", value):           # identifiers, keys, file types, codecs
        return False
    if re.fullmatch(r"[A-Za-z]+\.[A-Za-z0-9.]+", value):  # dotted names
        return False
    if any(token in value for token in ["=", "\\\\", "{\\", "[0:", "0x", "%@", "%03d", "%.", "%d", "dylib", "xmlns"]):
        return False
    if re.fullmatch(r"[A-Z0-9 .·×:+]{1,6}", value):      # format badges like PNG, 4:3
        return False
    return value[0].isupper() or value[0] in "\\(" or value.startswith(("Drag", "Press"))


def wrap():
    changed = 0
    for path in ROOT.rglob("*.swift"):
        if path.name in ("L10n.swift",):
            continue
        source = path.read_text()
        out, last = [], 0
        for start, end, value in literals(source):
            before = source[max(0, start - 40):start]
            if not any(before.endswith(call) for call in CALLS):
                continue
            if before.endswith("L10n.t(") or not looks_like_text(value):
                continue
            out.append(source[last:start])
            out.append(f"L10n.t({source[start:end]})")
            last = end
            changed += 1
        if out:
            out.append(source[last:])
            path.write_text("".join(out))
    print(f"wrapped {changed} strings")


def extract():
    found_static, found_dynamic = set(), set()
    for path in ROOT.rglob("*.swift"):
        if path.name in ("L10n.swift", "CommandLineRunner.swift", "DebugLog.swift"):
            continue
        source = path.read_text()
        for start, end, value in literals(source):
            context = source[max(0, start - 60):start]
            if "DebugLog.write(" in context or "fputs(" in context or "print(" in context[-12:]:
                continue
            if not looks_like_text(value):
                continue
            (found_dynamic if "\\(" in value else found_static).add(value)
    json.dump({"static": sorted(found_static), "dynamic": sorted(found_dynamic)}, sys.stdout, ensure_ascii=False, indent=1)


if __name__ == "__main__":
    {"wrap": wrap, "extract": extract}[sys.argv[1]]()
