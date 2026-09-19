"""Brightens the greyscale chemfire flame sprites.

The chemfire RSI is authored in greyscale and modulated at runtime by each chemfire prototype's `color`,
so anything done here multiplies straight through into every chemical fire in the game. The art sits on a
small palette of flat tones; a gamma curve lifts the dark ones (which read as muddy once a saturated
colour is multiplied over them) while leaving the near-white highlights alone, which a flat multiply or a
brightness offset would blow out instead.

Applied in place, so it compounds if you run it twice - `git checkout` the RSI to start over.

Usage:
    python Tools/_KS14/brighten_chemfire.py --dry-run     # print the tone mapping, touch nothing
    python Tools/_KS14/brighten_chemfire.py               # apply the default curve
    python Tools/_KS14/brighten_chemfire.py --gamma 1.7   # push it further
"""

import argparse
import json
from pathlib import Path

import numpy as np
from PIL import Image

DEFAULT_RSI = Path("Resources/Textures/_KS14/Effects/Fire/chemfire.rsi")

# `white_fx-*` and `white_full` are unused placeholders - leave them as the artist left them.
DEFAULT_PREFIXES = ("white_under-", "white_over-")


def build_lookup(gamma: float, gain: float) -> np.ndarray:
    """The 0-255 tone curve, precomputed so every image is a single array index."""
    ramp = np.arange(256, dtype=np.float64) / 255.0
    curved = np.power(ramp, 1.0 / gamma) * gain

    return np.clip(curved * 255.0, 0.0, 255.0).round().astype(np.uint8)


def collect_states(rsi_path: Path, prefixes: tuple[str, ...]) -> list[str]:
    """State names from meta.json, so states the RSI does not declare are never touched."""
    with (rsi_path / "meta.json").open(encoding="utf-8") as meta_file:
        meta = json.load(meta_file)

    return [
        state["name"]
        for state in meta["states"]
        if not prefixes or state["name"].startswith(prefixes)
    ]


def brighten_image(path: Path, lookup: np.ndarray, dry_run: bool) -> set[tuple[int, int]]:
    """Maps every RGB channel through the curve, leaving alpha untouched. Returns the tones it saw."""
    with Image.open(path) as image:
        pixels = np.asarray(image.convert("RGBA"))

    rgb = pixels[..., :3]
    opaque = pixels[..., 3] > 0

    tones = {(int(tone), int(lookup[tone])) for tone in np.unique(rgb[opaque])} if opaque.any() else set()

    if dry_run:
        return tones

    brightened = pixels.copy()
    brightened[..., :3] = lookup[rgb]
    Image.fromarray(brightened, mode="RGBA").save(path)

    return tones


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--rsi", type=Path, default=DEFAULT_RSI, help="RSI directory to process.")
    parser.add_argument("--gamma", type=float, default=1.4, help="Above 1 brightens, below 1 darkens.")
    parser.add_argument("--gain", type=float, default=1.0, help="Flat multiplier applied after the curve.")
    parser.add_argument(
        "--prefixes",
        nargs="*",
        default=list(DEFAULT_PREFIXES),
        help="Only states starting with one of these are touched. Pass none to process every state.",
    )
    parser.add_argument("--dry-run", action="store_true", help="Report the tone mapping without writing.")
    args = parser.parse_args()

    if args.gamma <= 0.0:
        parser.error("--gamma must be positive")

    lookup = build_lookup(args.gamma, args.gain)
    states = collect_states(args.rsi, tuple(args.prefixes))

    if not states:
        parser.error(f"no states in {args.rsi} matched {args.prefixes}")

    tones: set[tuple[int, int]] = set()
    for state in states:
        tones |= brighten_image(args.rsi / f"{state}.png", lookup, args.dry_run)

    verb = "would remap" if args.dry_run else "remapped"
    print(f"{verb} {len(states)} states in {args.rsi} (gamma {args.gamma}, gain {args.gain}):")
    for old, new in sorted(tones):
        print(f"  {old:>3} -> {new:>3}  ({new - old:+d})")


if __name__ == "__main__":
    main()
