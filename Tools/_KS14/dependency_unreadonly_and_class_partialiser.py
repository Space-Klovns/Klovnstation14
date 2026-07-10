import os
import re
import sys

# Matches class declarations.
# Groups:
#   1 = everything before "class"
#   2 = class name
#   3 = inheritance/etc.
CLASS_REGEX = re.compile(
    r'^(\s*(?:public|protected|internal|private|file|abstract|sealed|static|unsafe|new|\s)+)'
    r'class\s+([A-Za-z_][A-Za-z0-9_]*)'
    r'([^{\n]*)',
    re.MULTILINE
)

DEPENDENCY_ATTR = re.compile(r'^\s*\[Dependency(?:\(.*?\))?\]', re.MULTILINE)


def process_file(path: str):
    with open(path, "r", encoding="utf-8", newline="") as f:
        text = f.read()

    original = text

    # Find every [Dependency] occurrence
    dep_matches = list(DEPENDENCY_ATTR.finditer(text))
    if not dep_matches:
        return False

    # Remove readonly from dependency fields
    for match in reversed(dep_matches):
        start = match.end()

        # Find end of field (;)
        semi = text.find(";", start)
        if semi == -1:
            continue

        field = text[start:semi + 1]

        new_field = re.sub(r"\breadonly\s+", "", field)

        text = text[:start] + new_field + text[semi + 1:]

    # If any dependency exists, make containing classes partial.
    # (Good enough for normal C# formatting.)
    class_matches = list(CLASS_REGEX.finditer(text))

    for match in reversed(class_matches):
        class_start = match.end()

        # Find body start
        brace = text.find("{", class_start)
        if brace == -1:
            continue

        # Find matching closing brace
        depth = 1
        i = brace + 1
        while i < len(text) and depth:
            if text[i] == "{":
                depth += 1
            elif text[i] == "}":
                depth -= 1
            i += 1

        body = text[brace:i]

        if "[Dependency" not in body:
            continue

        decl = match.group(0)

        if re.search(r"\bpartial\b", decl):
            continue

        new_decl = re.sub(r"\bclass\b", "partial class", decl, count=1)

        text = text[:match.start()] + new_decl + text[match.end():]

    if text != original:
        with open(path, "w", encoding="utf-8", newline="") as f:
            f.write(text)
        print(f"Modified {path}")
        return True

    return False


def main(root):
    changed = 0

    for dirpath, _, filenames in os.walk(root):
        for filename in filenames:
            if filename.endswith(".cs"):
                if process_file(os.path.join(dirpath, filename)):
                    changed += 1

    print(f"\nDone. Modified {changed} files.")


if __name__ == "__main__":
    if len(sys.argv) > 1:
        root = sys.argv[1]
    else:
        root = os.getcwd()

    main(root)
