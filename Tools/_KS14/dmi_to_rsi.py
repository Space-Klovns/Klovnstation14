#!/usr/bin/env python3

## Logic in this file was ported from https://github.com/space-wizards/RSIEdit and its RSI.NET submodule
##      (https://github.com/space-wizards/RSI.NET), both licensed under the MIT license. See .claude/commands/dmi-to-rsi.md for usage.

"""Convert a BYOND DreamMaker Image (.dmi) into a Robust Station Image (.rsi directory).

A port of the DMI import in space-wizards/RSIEdit, which does the work in its RSI.NET submodule:
    RSI.NET  SpaceWizards.RsiLib/DMI/Metadata/RawMetadata.cs   - metadata parsing
             SpaceWizards.RsiLib/DMI/Metadata/Metadata.cs      - ToRsi: cutting cells out of the sheet
             SpaceWizards.RsiLib/DMI/DmiState.cs               - rewind, delays per direction
             SpaceWizards.RsiLib/RSI/RsiState.cs               - state names, GetFullImage sheet layout
             SpaceWizards.RsiLib/RSI/Rsi.cs                    - meta.json writing
    RSIEdit  Editor/ViewModels/MainWindowViewModel.cs          - TryPasteDmiUrl: copyright, ReformatAllNames

Usage:
    python Tools/_KS14/dmi_to_rsi.py <input.dmi> <output-dir> [--license L] [--copyright TEXT]
                                      [--repo URL --commit HASH [--repo-path PATH]] [--keep-names] [--indent N] [--force]

Requires Pillow.
"""

from __future__ import annotations

import argparse
import json
import math
import os
import sys
from dataclasses import dataclass, field

from PIL import Image

DMI_HEADER = "BEGIN DMI"
DEFAULT_LICENSE = "CC-BY-SA-3.0"
VALID_DIRECTIONS = (1, 4, 8)  # RSI.NET DirectionType: None = 1, Cardinal = 4, Diagonal = 8
DEFAULT_DELAY = 0.1  # RawMetadata: a state with frames but no delays gets 1 tick (0.1 s) per frame

# Path.GetInvalidFileNameChars() on Windows, which is where RSIEdit's output usually has to be valid:
# control characters plus " < > | : * ? \ /
INVALID_FILENAME_CHARS = set('"<>|:*?\\/') | {chr(i) for i in range(32)}


def warn(message: str) -> None:
    print(f"warning: {message}", file=sys.stderr)


@dataclass
class DmiState:
    name: str
    directions: int = 1
    frames: int = 1
    delays: list[float] | None = None
    rewind: bool = False
    # Parsed for reporting only - an RSI has nowhere to put them.
    movement: bool = False
    loop: int | None = None
    hotspot: str | None = None
    extra: dict[str, str] = field(default_factory=dict)


@dataclass
class DmiMetadata:
    version: float
    width: int
    height: int
    states: list[DmiState]


# ---------------------------------------------------------------------------------------------------------------------
# Metadata
# ---------------------------------------------------------------------------------------------------------------------

def read_dmi_description(image: Image.Image) -> str:
    """The text chunk (tEXt/zTXt/iTXt) holding '# BEGIN DMI'. RSIEdit takes the first tag that contains it."""
    texts = dict(getattr(image, "text", {}) or {})
    texts.update({k: v for k, v in image.info.items() if isinstance(v, str)})
    for value in texts.values():
        if DMI_HEADER in value:
            return value
    raise ValueError("No dmi tag found")


def _split_tuple(line: str) -> tuple[str, str] | None:
    """RawMetadata.Tuple: comments yield nothing; otherwise split on the first '=' and strip quotes."""
    if line.startswith("#"):
        return None
    if "=" not in line:
        return None
    key, value = line.split("=", 1)
    # C# Trim('"') strips every leading and trailing quote, as str.strip('"') does.
    return key.strip().strip('"'), value.strip().strip('"')


def parse_metadata(description: str) -> DmiMetadata:
    lines = [line.strip() for line in description.split("\n")]
    lines = [line for line in lines if line]

    # Lines[0] is '# BEGIN DMI'; RSIEdit calls Next() before reading the version.
    index = 1
    if index >= len(lines):
        raise ValueError("No version found")
    version_tuple = _split_tuple(lines[index])
    if version_tuple is None or version_tuple[0] != "version":
        raise ValueError("No version found")
    try:
        version = float(version_tuple[1])
    except ValueError:
        raise ValueError("No version found") from None

    width = height = 32  # BYOND's default icon size, used when the header leaves it out
    index += 1
    while index < len(lines):
        entry = _split_tuple(lines[index])
        if entry is None or entry[0] not in ("width", "height"):
            break
        try:
            if entry[0] == "width":
                width = int(entry[1])
            else:
                height = int(entry[1])
        except ValueError:
            pass
        index += 1

    states: list[DmiState] = []
    while index < len(lines):
        entry = _split_tuple(lines[index])
        index += 1
        if entry is None or entry[0] != "state":
            continue

        state = DmiState(name=entry[1])
        while index < len(lines):
            entry = _split_tuple(lines[index])
            if entry is None or entry[0] == "state":
                break
            key, value = entry
            index += 1
            if key == "dirs":
                try:
                    dirs = int(value)
                except ValueError:
                    continue
                if dirs in VALID_DIRECTIONS:
                    state.directions = dirs
                else:
                    warn(f"state '{state.name}': unsupported dirs = {value}, treated as 1 (as RSIEdit does)")
            elif key == "frames":
                try:
                    state.frames = int(value)
                except ValueError:
                    continue
            elif key == "delay":
                delays = []
                for part in value.split(","):
                    try:
                        delays.append(float(part) / 10.0)  # ticks (deciseconds) -> seconds
                    except ValueError:
                        continue
                state.delays = delays
            elif key == "rewind":
                # RSIEdit treats any rewind line as true; BYOND only ever writes 'rewind = 1'.
                state.rewind = value.strip() != "0"
            elif key == "movement":
                state.movement = value.strip() != "0"
            elif key == "loop":
                try:
                    state.loop = int(value)
                except ValueError:
                    pass
            elif key == "hotspot":
                state.hotspot = value
            else:
                state.extra[key] = value

        if state.frames > 1 and state.delays is None:
            state.delays = [DEFAULT_DELAY] * state.frames
        if state.delays is not None and len(state.delays) > state.frames:
            del state.delays[state.frames:]
        if state.delays is not None and len(state.delays) < state.frames:
            # RSIEdit would throw here (it sizes the frame array by the delay count); pad instead.
            warn(f"state '{state.name}': {len(state.delays)} delays for {state.frames} frames, "
                 f"padding with {DEFAULT_DELAY}s")
            state.delays += [DEFAULT_DELAY] * (state.frames - len(state.delays))
        states.append(state)

    if width <= 0 or height <= 0:
        raise ValueError(f"Invalid icon size {width}x{height}")
    return DmiMetadata(version, width, height, states)


# ---------------------------------------------------------------------------------------------------------------------
# Names
# ---------------------------------------------------------------------------------------------------------------------

def sanitize_name(name: str, reformat: bool) -> str:
    # RsiState constructor: split on invalid filename chars, join with '_'.
    name = "".join("_" if c in INVALID_FILENAME_CHARS else c for c in name)
    if reformat:
        # MainWindowViewModel.ReformatAllNames, run after a URL import.
        name = name.replace(" ", "_").lower()
    return name


def assign_names(states: list[DmiState], reformat: bool) -> list[str]:
    """RSIEdit leaves empty and duplicate names alone, which writes '.png' or overwrites a state's PNG.
    Both make an RSI SS14 can't load correctly, so they are renamed here, with a warning."""
    names: list[str] = []
    taken: set[str] = set()  # compared case-insensitively: Windows filesystems are
    for state in states:
        name = sanitize_name(state.name, reformat)
        if not name:
            name = "empty"
            warn(f"state with an empty name renamed to '{name}'")
        if name.lower() in taken and state.movement:
            name += "_movement"
        if name.lower() in taken:
            base = name
            suffix = 2
            while f"{base}_{suffix}".lower() in taken:
                suffix += 1
            name = f"{base}_{suffix}"
        if name != sanitize_name(state.name, reformat) and sanitize_name(state.name, reformat):
            warn(f"duplicate state '{state.name}'{' (movement)' if state.movement else ''} renamed to '{name}'")
        taken.add(name.lower())
        names.append(name)
    return names


# ---------------------------------------------------------------------------------------------------------------------
# Images
# ---------------------------------------------------------------------------------------------------------------------

def cut_frames(meta: DmiMetadata, sheet: Image.Image) -> list[list[list[Image.Image]]]:
    """Metadata.ToRsi: per state, frames[direction][frame].

    Cells are read left to right, top to bottom, continuing across states; within a state the
    order is frame-major, then direction (S, N, E, W, SE, SW, NE, NW)."""
    x = y = 0
    result = []
    for state in meta.states:
        frames = [[None] * state.frames for _ in range(state.directions)]
        for frame in range(state.frames):
            for direction in range(state.directions):
                if y + meta.height > sheet.height:
                    raise ValueError(f"state '{state.name}' runs past the end of the sheet")
                frames[direction][frame] = sheet.crop((x, y, x + meta.width, y + meta.height))
                x += meta.width
                if x >= sheet.width:
                    x = 0
                    y += meta.height

        if state.rewind and state.frames > 1:
            # 0 1 2 -> 0 1 2 1 0: the frames before the last, played backwards.
            for direction in range(state.directions):
                frames[direction] += frames[direction][-2::-1]
            # DmiState.ToRsiState mirrors the delays the same way.
            state.delays = state.delays + state.delays[-2::-1]
        result.append(frames)

    used_rows = y + (meta.height if x else 0)
    if used_rows < sheet.height:
        warn(f"sheet has {sheet.height - used_rows}px of rows after the last state's cells")
    return result


def rows_and_columns(images: int) -> tuple[int, int]:
    """RsiState.GetRowsAndColumns. C# Math.Round is banker's rounding, as is Python's round()."""
    root = math.sqrt(images)
    return math.ceil(root), round(root)


def build_sheet(frames: list[list[Image.Image]], width: int, height: int) -> Image.Image:
    """RsiState.GetFullImage: direction-major (all of direction 0's frames, then direction 1's...)."""
    cells = [cell for direction in frames for cell in direction]
    rows, columns = rows_and_columns(len(cells))
    sheet = Image.new("RGBA", (width * columns, height * rows), (0, 0, 0, 0))
    for i, cell in enumerate(cells):
        sheet.paste(cell, ((i % columns) * width, (i // columns) * height))
    return sheet


# ---------------------------------------------------------------------------------------------------------------------
# Copyright
# ---------------------------------------------------------------------------------------------------------------------

def find_repo_relative_path(dmi_path: str) -> str | None:
    """The DMI's path inside its git checkout, found by walking up to the folder holding '.git'."""
    directory = os.path.dirname(os.path.abspath(dmi_path))
    while True:
        if os.path.exists(os.path.join(directory, ".git")):
            return os.path.relpath(os.path.abspath(dmi_path), directory).replace(os.sep, "/")
        parent = os.path.dirname(directory)
        if parent == directory:
            return None
        directory = parent


def rsiedit_copyright(repo_url: str, commit: str, repo_path: str) -> str:
    """MainWindowViewModel.TryPasteDmiUrl: $"Taken from {codebase} at {link}", where link is the
    file's GitHub blob URL with the branch replaced by the commit hash."""
    repo_url = repo_url.strip().rstrip("/")
    if repo_url.endswith(".git"):
        repo_url = repo_url[:-4]
    codebase = repo_url.split("/")[-1]
    link = f"{repo_url}/blob/{commit}/{repo_path.lstrip('/')}"
    return f"Taken from {codebase} at {link}"


# ---------------------------------------------------------------------------------------------------------------------
# meta.json
# ---------------------------------------------------------------------------------------------------------------------

def _json_number(value: float) -> float | int:
    # System.Text.Json writes the shortest round-trip form of a float and no '.0' on whole numbers.
    value = float(f"{value:.7g}")  # back to single precision, as RSI.NET stores delays as float
    return int(value) if value.is_integer() else value


def build_meta(meta: DmiMetadata, names: list[str], license_: str | None, copyright_: str | None) -> dict:
    states = []
    for state, name in zip(meta.states, names):
        entry: dict = {"name": name}
        if state.directions != 1:
            entry["directions"] = state.directions
        if state.delays is not None:
            per_direction = [[_json_number(d) for d in state.delays] for _ in range(state.directions)]
            # Rsi.OmitDefaultDelays: a lone 1 s delay is the default, so all-[1] delays are dropped.
            if not all(d == [1] for d in per_direction):
                entry["delays"] = [[] if d == [1] else d for d in per_direction]
        states.append(entry)

    data: dict = {"version": 1}
    if license_ is not None:
        data["license"] = license_
    if copyright_ is not None:
        data["copyright"] = copyright_
    data["size"] = {"x": meta.width, "y": meta.height}
    data["states"] = states
    return data


# ---------------------------------------------------------------------------------------------------------------------
# CLI
# ---------------------------------------------------------------------------------------------------------------------

def convert(args: argparse.Namespace) -> str:
    with Image.open(args.input) as source:
        description = read_dmi_description(source)
        sheet = source.convert("RGBA")
    meta = parse_metadata(description)
    if meta.version != 4.0:
        warn(f"DMI version {meta.version}; only 4.0 has been checked")

    for state in meta.states:
        ignored = []
        if state.movement:
            ignored.append("movement")
        if state.loop is not None:
            ignored.append(f"loop = {state.loop}")
        if state.hotspot is not None:
            ignored.append(f"hotspot = {state.hotspot}")
        ignored += [f"{k} = {v}" for k, v in state.extra.items()]
        if ignored:
            warn(f"state '{state.name}': not representable in an RSI, ignored: {', '.join(ignored)}")

    copyright_ = args.copyright
    if copyright_ is None and args.repo and args.commit:
        repo_path = args.repo_path or find_repo_relative_path(args.input)
        if repo_path is None:
            raise SystemExit("error: --repo/--commit given but the DMI is not inside a git checkout; pass --repo-path")
        copyright_ = rsiedit_copyright(args.repo, args.commit, repo_path)
    elif copyright_ is None and (args.repo or args.commit):
        raise SystemExit("error: --repo and --commit must be given together")

    frames = cut_frames(meta, sheet)
    names = assign_names(meta.states, reformat=not args.keep_names)

    stem = os.path.splitext(os.path.basename(args.input))[0]
    rsi_dir = os.path.join(args.output_dir, f"{stem}.rsi")
    if os.path.exists(rsi_dir) and not args.force:
        raise SystemExit(f"error: {rsi_dir} already exists (pass --force to overwrite)")
    os.makedirs(rsi_dir, exist_ok=True)

    for state_frames, name in zip(frames, names):
        build_sheet(state_frames, meta.width, meta.height).save(os.path.join(rsi_dir, f"{name}.png"), optimize=True)

    data = build_meta(meta, names, args.license, copyright_)
    with open(os.path.join(rsi_dir, "meta.json"), "w", encoding="utf-8", newline="\n") as file:
        json.dump(data, file, indent=args.indent, ensure_ascii=False)
        file.write("\n")
    return rsi_dir


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Convert a BYOND .dmi into an SS14 .rsi directory (port of RSIEdit's DMI import).")
    parser.add_argument("input", help="the .dmi file")
    parser.add_argument("output_dir", help="directory to write <dmi stem>.rsi into")
    parser.add_argument("--license", default=DEFAULT_LICENSE, help=f"RSI license (default {DEFAULT_LICENSE})")
    parser.add_argument("--copyright", help="RSI copyright text; overrides --repo/--commit")
    parser.add_argument("--repo", help="source repository URL, e.g. https://github.com/tgstation/tgstation")
    parser.add_argument("--commit", help="commit hash the DMI was taken from")
    parser.add_argument("--repo-path", help="the DMI's path inside the repository (default: found from the enclosing .git)")
    parser.add_argument("--keep-names", action="store_true",
                        help="keep state names' case and spaces (default lowercases and turns spaces into '_', as RSIEdit's URL import does)")
    parser.add_argument("--indent", type=int, default=2, help="meta.json indentation (default 2, as SS14's meta.json files use)")
    parser.add_argument("--force", action="store_true", help="write into an existing .rsi directory")
    args = parser.parse_args(argv)

    try:
        rsi_dir = convert(args)
    except ValueError as error:
        print(f"error: {error}", file=sys.stderr)
        return 1
    print(rsi_dir)
    return 0


if __name__ == "__main__":
    sys.exit(main())
