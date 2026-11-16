#!/usr/bin/env python3
# KS14: added in this fork
"""
Checks that every literal locale key the cultivation C# asks for exists in an .ftl file. A missing key compiles,
passes every test and only shows up in game as a raw key, so run this after touching strings:

    python3 Tools/_KS14/check_cultivation_locale.py

Interpolated keys ($"cultivation-realm-{...}") are covered by CultivationLocaleTest instead.
"""

import glob
import re
import subprocess
import sys

keys = set()
for path in glob.glob("Resources/Locale/en-US/**/*.ftl", recursive=True):
    with open(path, encoding="utf-8") as file:
        for line in file:
            match = re.match(r"^([A-Za-z0-9][A-Za-z0-9-]*)\s*=", line)
            if match:
                keys.add(match.group(1))

files = subprocess.check_output(["git", "ls-files", "Content.*/_KS14/Cultivation/*"]).decode().split()
missing = []
for path in files:
    if not path.endswith(".cs"):
        continue
    with open(path, encoding="utf-8") as file:
        source = file.read()
    used = re.findall(r'GetString\(\s*"([a-z0-9-]+)"', source)
    used += re.findall(r'\(\s*"([a-z0-9]+(?:-[a-z0-9]+)+)"\s*[,)]', source)
    missing += [(path, key) for key in sorted(set(used)) if key not in keys]

for path, key in missing:
    print(f"{path}: missing locale key '{key}'")
sys.exit(1 if missing else 0)
