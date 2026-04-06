#!/usr/bin/env python3
# remove_reuse_headers.py - Remove REUSE headers from source files

import os
import re
import sys
import subprocess
from pathlib import Path

# --- Configuration ---
REPO_PATH = "."
MAX_WORKERS = os.cpu_count() or 4

FILE_PATTERNS = [
    "*.cs", "*.js", "*.ts", "*.jsx", "*.tsx", "*.c", "*.cpp", "*.cc", "*.h", "*.hpp",
    "*.java", "*.scala", "*.kt", "*.swift", "*.go", "*.rs", "*.dart", "*.groovy", "*.php",
    "*.yaml", "*.yml", "*.ftl", "*.py", "*.rb", "*.pl", "*.pm", "*.sh", "*.bash", "*.zsh",
    "*.fish", "*.ps1", "*.r", "*.rmd", "*.jl", "*.tcl", "*.perl", "*.conf", "*.toml",
    "*.ini", "*.cfg", "*.bat", "*.cmd", "*.vb", "*.vbs", "*.bas", "*.asm", "*.s", "*.lisp",
    "*.clj", "*.f", "*.f90", "*.m", "*.sql", "*.ada", "*.adb", "*.ads", "*.hs", "*.lhs",
    "*.lua", "*.xaml", "*.xml", "*.html", "*.htm", "*.svg", "*.css", "*.scss", "*.sass",
    "*.less", "*.md", "*.markdown", "*.csproj", "*.DotSettings", ".gitignore", ".dockerignore"
]

COMMENT_STYLES = {
    ".cs": ("//", None),
    ".js": ("//", None),
    ".ts": ("//", None),
    ".jsx": ("//", None),
    ".tsx": ("//", None),
    ".c": ("//", None),
    ".cpp": ("//", None),
    ".cc": ("//", None),
    ".h": ("//", None),
    ".hpp": ("//", None),
    ".java": ("//", None),
    ".scala": ("//", None),
    ".kt": ("//", None),
    ".swift": ("//", None),
    ".go": ("//", None),
    ".rs": ("//", None),
    ".dart": ("//", None),
    ".groovy": ("//", None),
    ".php": ("//", None),

    ".yaml": ("#", None),
    ".yml": ("#", None),
    ".ftl": ("#", None),
    ".py": ("#", None),
    ".rb": ("#", None),
    ".pl": ("#", None),
    ".pm": ("#", None),
    ".sh": ("#", None),
    ".bash": ("#", None),
    ".zsh": ("#", None),
    ".fish": ("#", None),
    ".ps1": ("#", None),
    ".r": ("#", None),
    ".rmd": ("#", None),
    ".jl": ("#", None),
    ".tcl": ("#", None),
    ".perl": ("#", None),
    ".conf": ("#", None),
    ".toml": ("#", None),
    ".ini": ("#", None),
    ".cfg": ("#", None),
    ".gitignore": ("#", None),
    ".dockerignore": ("#", None),

    ".bat": ("REM", None),
    ".cmd": ("REM", None),
    ".vb": ("'", None),
    ".vbs": ("'", None),
    ".bas": ("'", None),
    ".asm": (";", None),
    ".s": (";", None),
    ".lisp": (";", None),
    ".clj": (";", None),
    ".f": ("!", None),
    ".f90": ("!", None),
    ".m": ("%", None),
    ".sql": ("--", None),
    ".ada": ("--", None),
    ".adb": ("--", None),
    ".ads": ("--", None),
    ".hs": ("--", None),
    ".lhs": ("--", None),
    ".lua": ("--", None),

    ".xaml": ("<!--", "-->"),
    ".xml": ("<!--", "-->"),
    ".html": ("<!--", "-->"),
    ".htm": ("<!--", "-->"),
    ".svg": ("<!--", "-->"),
    ".css": ("/*", "*/"),
    ".scss": ("/*", "*/"),
    ".sass": ("/*", "*/"),
    ".less": ("/*", "*/"),
    ".md": ("<!--", "-->"),
    ".markdown": ("<!--", "-->"),
    ".csproj": ("<!--", "-->"),
    ".DotSettings": ("<!--", "-->"),
}

LICENSE_IDS = ("MIT", "MPL-2.0", "AGPL-3.0-or-later", "CC-BY-NC-SA-3.0")


def run_git_command(command, cwd=REPO_PATH, check=True):
    try:
        result = subprocess.run(
            command,
            capture_output=True,
            text=True,
            check=check,
            cwd=cwd,
            encoding="utf-8",
            errors="ignore",
        )
        return result.stdout.strip()
    except subprocess.CalledProcessError as e:
        if check:
            print(f"Error running git command {' '.join(command)}: {e.stderr}", file=sys.stderr)
        return None
    except FileNotFoundError:
        print("FATAL: git not found.", file=sys.stderr)
        return None


def get_target_files():
    cmd = ["git", "ls-files", *FILE_PATTERNS]
    output = run_git_command(cmd, check=False)
    if not output:
        return []
    return [line.strip() for line in output.splitlines() if line.strip()]


def strip_single_line_header(lines, start_index, prefix):
    copyright_re = re.compile(
        rf"^\s*{re.escape(prefix)}\s*SPDX-FileCopyrightText:\s*\d{{4}}\s+.+\s*$"
    )
    license_re = re.compile(
        rf"^\s*{re.escape(prefix)}\s*SPDX-License-Identifier:\s*(?:"
        + "|".join(re.escape(x) for x in LICENSE_IDS)
        + r")\s*$"
    )
    separator_re = re.compile(rf"^\s*{re.escape(prefix)}\s*$")

    i = start_index
    seen_spdx = False

    while i < len(lines):
        text = lines[i].rstrip("\r\n")
        if copyright_re.match(text) or license_re.match(text) or separator_re.match(text):
            seen_spdx = True
            i += 1
            continue
        break

    if not seen_spdx:
        return None

    while i < len(lines) and lines[i].strip() == "":
        i += 1

    return i


def strip_multiline_header(lines, start_index, prefix, suffix):
    if start_index >= len(lines):
        return None

    if lines[start_index].strip() != prefix:
        return None

    i = start_index + 1
    seen_spdx = False

    while i < len(lines):
        stripped = lines[i].strip()

        if stripped == suffix:
            i += 1
            break

        if stripped.startswith("SPDX-FileCopyrightText:") or stripped.startswith("SPDX-License-Identifier:"):
            seen_spdx = True
            i += 1
            continue

        if stripped == "":
            i += 1
            continue

        return None

    if not seen_spdx:
        return None

    while i < len(lines) and lines[i].strip() == "":
        i += 1

    return i


def strip_reuse_header(content, comment_style):
    prefix, suffix = comment_style
    lines = content.splitlines(keepends=True)

    if not lines:
        return content, False

    # Preserve a shebang or XML declaration if present.
    keep_prefix = 0
    if lines[0].startswith("#!"):
        keep_prefix = 1
    elif lines[0].lstrip().startswith("<?xml"):
        keep_prefix = 1

    start = keep_prefix

    # Allow blank lines between preamble and header, but only remove them if we actually find a header.
    candidate = start
    while candidate < len(lines) and lines[candidate].strip() == "":
        candidate += 1

    if suffix is None:
        end = strip_single_line_header(lines, candidate, prefix)
    else:
        end = strip_multiline_header(lines, candidate, prefix, suffix)

    if end is None:
        return content, False

    new_lines = lines[:keep_prefix] + lines[end:]
    new_content = "".join(new_lines)
    return new_content, new_content != content


def process_file(file_path):
    _, ext = os.path.splitext(file_path)
    comment_style = COMMENT_STYLES.get(ext)
    if not comment_style:
        return "skipped"

    full_path = Path(REPO_PATH) / file_path
    if not full_path.exists():
        return "not_found"

    with open(full_path, "r", encoding="utf-8-sig", errors="ignore") as f:
        content = f.read()

    new_content, changed = strip_reuse_header(content, comment_style)

    if not changed:
        return "no_change"

    with open(full_path, "w", encoding="utf-8", newline="\n") as f:
        f.write(new_content)

    return "updated"


if __name__ == "__main__":
    print("Removing REUSE headers...")

    files = get_target_files()
    if not files:
        print("No matching files found.")
        sys.exit(0)

    updated = 0
    skipped = 0
    not_found = 0
    no_change = 0

    for file_path in files:
        result = process_file(file_path)
        if result == "updated":
            updated += 1
            print(f"Updated: {file_path}")
        elif result == "not_found":
            not_found += 1
        elif result == "no_change":
            no_change += 1
        else:
            skipped += 1

    print("\n--- Summary ---")
    print(f"Updated:   {updated}")
    print(f"No change: {no_change}")
    print(f"Skipped:   {skipped}")
    print(f"Missing:   {not_found}")
