#!/usr/bin/env python3
"""
Bakes the cultivation sound cues out of the SurfShack13 samples they were played from there.

SS13 layered several samples at different speeds and delays at runtime (cultivation_great_bell, cultivation_guqin_phrase
and friends in code/modules/cultivation/visuals.dm). SS14 plays one file per call, so the layers are mixed down here,
once, with ffmpeg.

Usage: cultivation_sounds.py <path to the SurfShack13 checkout's sound folder>
"""
import os
import subprocess
import sys

OUT = os.path.join(os.path.dirname(__file__), "..", "..", "Resources", "Audio", "_KS14", "Cultivation")

CELESTE = "runtime/instruments/synthesis_samples/chromatic/fluid_celeste/{}.ogg"
NYLON = "runtime/instruments/synthesis_samples/guitar/crisis_nylon/c4.ogg"
# Gong scale (do re mi sol la do), as playback speeds relative to the sample's root note.
PENTATONIC = [1, 1.122, 1.26, 1.498, 1.682, 2]

# Each cue: a list of (sample, speed, volume, delay in seconds).
CUES = {
    # A great temple bell struck by a giant: a deep bell, a slow booming gong, the thud of impact, a ringing overtone.
    "great_bell": [
        (CELESTE.format("C2"), 1.0, 1.0, 0.0),
        ("effects/gong.ogg", 0.35, 1.0, 0.0),
        ("effects/explosion/explosion_distant.ogg", 1.0, 0.7, 0.0),
        (CELESTE.format("C4"), 1.0, 0.5, 0.35),
    ],
    # A low gong under a soft celeste chime, like a struck singing bowl.
    "temple_bell": [
        ("effects/gong.ogg", 0.6, 1.0, 0.0),
        (CELESTE.format("C3"), 1.0, 1.0, 0.0),
    ],
    # Plucked strings walking up the scale.
    "guqin_rise": [(NYLON, PENTATONIC[note - 1], 0.8, i * 0.18) for i, note in enumerate([1, 2, 3, 5])],
    "guqin_breakthrough": [(NYLON, PENTATONIC[note - 1], 0.8, i * 0.18) for i, note in enumerate([1, 2, 3, 4, 5, 6])],
    "guqin_fall": [(NYLON, PENTATONIC[note - 1], 0.8, i * 0.22) for i, note in enumerate([5, 3, 2, 1])],
    # High celeste notes, loosely spaced.
    "wind_chimes": [
        (CELESTE.format("C6"), 1.0, 0.6, 0.0),
        (CELESTE.format("C7"), 1.26, 0.5, 0.2),
        (CELESTE.format("C6"), 1.498, 0.5, 0.5),
        (CELESTE.format("C7"), 1.122, 0.4, 0.7),
    ],
    # Air, then robes snapping.
    "qinggong": [
        ("items/weapons/fwoosh.ogg", 1.3, 0.9, 0.0),
        ("effects/rustle/rustle1.ogg", 1.0, 0.6, 0.05),
    ],
    # A mountain coming down: rock breaking under a meteor's thud and a far explosion.
    "mountain_fall": [
        ("effects/meteorimpact.ogg", 0.8, 1.0, 0.0),
        ("effects/rock/rock_break.ogg", 0.7, 0.9, 0.05),
        ("effects/explosion/explosion_distant.ogg", 1.0, 0.8, 0.1),
    ],
    # The breakthrough swell: a charge, the strings, a temple bell after.
    "ascend": [
        ("effects/magic/charge.ogg", 1.0, 0.9, 0.0),
        ("effects/gong.ogg", 0.6, 0.8, 1.1),
        (CELESTE.format("C3"), 1.0, 0.8, 1.1),
    ] + [(NYLON, PENTATONIC[note - 1], 0.7, 0.2 + i * 0.18) for i, note in enumerate([1, 2, 3, 4, 5, 6])],
}


def bake(sound_root: str, name: str, layers) -> None:
    args = ["ffmpeg", "-y", "-loglevel", "error"]
    filters = []
    for index, (sample, speed, volume, delay) in enumerate(layers):
        args += ["-i", os.path.join(sound_root, sample)]
        delay_ms = int(delay * 1000)
        filters.append(
            f"[{index}:a]aformat=sample_rates=44100:channel_layouts=mono,asetrate={int(44100 * speed)},aresample=44100,"
            f"volume={volume},adelay={delay_ms}:all=1[a{index}]")
    mix = "".join(f"[a{index}]" for index in range(len(layers)))
    filters.append(f"{mix}amix=inputs={len(layers)}:normalize=0:duration=longest,alimiter=limit=0.95[out]")
    out_path = os.path.join(OUT, f"{name}.ogg")
    args += ["-filter_complex", ";".join(filters), "-map", "[out]", "-ac", "1", "-c:a", "libvorbis", "-q:a", "4", out_path]
    subprocess.run(args, check=True)


def main() -> None:
    if len(sys.argv) != 2:
        sys.exit(__doc__)

    os.makedirs(OUT, exist_ok=True)
    for name, layers in CUES.items():
        bake(sys.argv[1], name, layers)
        print("baked", name)


if __name__ == "__main__":
    main()
