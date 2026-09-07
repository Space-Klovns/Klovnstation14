"""Normalises audio files to a common perceived loudness (EBU R128 integrated LUFS).

Ambience and music dropped into the repo tends to arrive mastered for streaming — around -14 LUFS,
peaking at 0 dBFS — which is enormously louder than the ambient beds already in game. Matching them by
ear one file at a time is guesswork, so this measures each file's integrated loudness with ffmpeg's
`ebur128` filter and applies a single, flat gain to hit the target.

The gain is flat on purpose: no compression, no limiting, no `loudnorm` two-pass dynamic reshaping. A
track that is quiet-then-loud should stay quiet-then-loud, and every file processed against the same
target keeps its own dynamics while sitting at the same perceived level as its neighbours.

Reference levels (integrated LUFS) for the ambient music already in the repo, for picking a target:

    Resources/Audio/_KS14/Ambience/Space/ambispace3.ogg    -28.8   (quiet end)
    Resources/Audio/_KS14/Ambience/Space/ambispace4.ogg    -28.0
    Resources/Audio/_KS14/Ambience/Space/ambispace5.ogg    -27.8
    Resources/Audio/_KS14/Ambience/Space/ambispace6.ogg    -24.4   (loud end)
    Resources/Audio/_KS14/Ambience/Space/ambispace.ogg     -24.5

Rather than hardcoding a number, point `--reference` at a file you already like the volume of and its
measured loudness becomes the target.

Requires ffmpeg and ffprobe on PATH.

Usage:

    # what would happen, measuring only - always start here
    python Tools/_KS14/normalize_loudness.py Resources/Audio/_KS14/Ambience/Foo --dry-run

    # match a file whose volume is already right
    python Tools/_KS14/normalize_loudness.py Resources/Audio/_KS14/Ambience/Foo \
        --reference Resources/Audio/_KS14/Ambience/Space/ambispace3.ogg

    # or name the target directly, and skip a file that is already fine
    python Tools/_KS14/normalize_loudness.py Resources/Audio/_KS14/Ambience/Foo \
        --target -26.0 --exclude medsci2.ogg
"""

import argparse
import re
import shutil
import subprocess
import sys
from pathlib import Path

AUDIO_SUFFIXES = {".ogg", ".wav", ".mp3", ".flac", ".opus"}

# Bitrates below this are treated as the file's real bitrate; anything above is ffprobe reporting the
# codec's VBR ceiling (libvorbis reports 499821 for quality-based encodes) rather than what was used.
VBR_CEILING_BITS = 400_000
# What to encode those VBR files at instead. High enough to not be the weak link in an ambient bed.
VBR_FALLBACK_BITRATE = "320k"
# Ceiling on the bitrate we will re-encode at, so a wasteful source does not stay wasteful.
MAX_BITRATE_BITS = 320_000

# ebur128 prints a summary block at the end; the integrated figure is the one line we need out of it.
INTEGRATED_PATTERN = re.compile(r"^\s*I:\s*(-?\d+(?:\.\d+)?)\s*LUFS", re.MULTILINE)
PEAK_PATTERN = re.compile(r"^\s*Peak:\s*(-?\d+(?:\.\d+)?)\s*dBFS", re.MULTILINE)


def run(command: list[str]) -> str:
    """Runs a command, returning its combined output, and dies loudly if it fails."""

    result = subprocess.run(command, capture_output=True, text=True)
    if result.returncode != 0:
        sys.exit(f"command failed: {' '.join(command)}\n{result.stderr.strip()}")

    return result.stdout + result.stderr


def measure(path: Path) -> tuple[float, float]:
    """Measures a file's integrated loudness in LUFS and its true peak in dBFS."""

    output = run([
        "ffmpeg", "-hide_banner", "-nostats",
        "-i", str(path),
        "-filter:a", "ebur128=peak=true",
        "-f", "null", "-",
    ])

    # Both patterns match repeatedly during the progress readout; the summary block is last.
    integrated = INTEGRATED_PATTERN.findall(output)
    peak = PEAK_PATTERN.findall(output)
    if not integrated:
        sys.exit(f"could not measure loudness of {path}")

    return float(integrated[-1]), float(peak[-1]) if peak else 0.0


def probe_stream(path: Path) -> dict[str, str]:
    """Reads back the encoding parameters of a file's first audio stream, so re-encoding can match."""

    output = run([
        "ffprobe", "-v", "error",
        "-select_streams", "a:0",
        "-show_entries", "stream=codec_name,sample_rate,channels,bit_rate",
        "-of", "default=noprint_wrappers=1",
        str(path),
    ])

    stream = {}
    for line in output.splitlines():
        if "=" in line:
            key, _, value = line.partition("=")
            stream[key.strip()] = value.strip()

    return stream


def resolve_bitrate(stream: dict[str, str]) -> str:
    """Picks the bitrate to re-encode at: the source's own, unless that is a VBR ceiling or excessive."""

    raw = stream.get("bit_rate", "N/A")
    if not raw.isdigit():
        return VBR_FALLBACK_BITRATE

    bits = int(raw)
    if bits >= VBR_CEILING_BITS:
        return VBR_FALLBACK_BITRATE

    return f"{min(bits, MAX_BITRATE_BITS) // 1000}k"


def collect_files(paths: list[Path], exclusions: set[str]) -> list[Path]:
    """Expands the given paths into a sorted list of audio files, dropping excluded names."""

    files: list[Path] = []
    for path in paths:
        if path.is_dir():
            files.extend(child for child in path.iterdir() if child.suffix.lower() in AUDIO_SUFFIXES)
        elif path.is_file():
            files.append(path)
        else:
            sys.exit(f"no such file or directory: {path}")

    return sorted({file for file in files if file.name not in exclusions})


def normalize(path: Path, gainDecibels: float) -> None:
    """Re-encodes a file in place with a flat gain applied, matching the source's encoding settings."""

    stream = probe_stream(path)
    codec = {"ogg": "libvorbis", "opus": "libopus", "mp3": "libmp3lame"}.get(path.suffix.lower().lstrip("."))
    if codec is None:
        codec = {"vorbis": "libvorbis", "opus": "libopus", "mp3": "libmp3lame"}.get(stream.get("codec_name", ""))
    if codec is None:
        sys.exit(f"don't know how to re-encode {path}")

    # The extension has to stay last - ffmpeg picks the output container from it.
    temporaryPath = path.with_name(f"{path.stem}.normalizing{path.suffix}")
    command = [
        "ffmpeg", "-v", "error", "-y",
        "-i", str(path),
        "-af", f"volume={gainDecibels:.2f}dB",
        "-c:a", codec,
        "-b:a", resolve_bitrate(stream),
    ]

    # Preserve the source's sample rate and channel count; a resample here would be an unasked-for change.
    if stream.get("sample_rate", "").isdigit():
        command += ["-ar", stream["sample_rate"]]
    if stream.get("channels", "").isdigit():
        command += ["-ac", stream["channels"]]

    command.append(str(temporaryPath))
    run(command)
    temporaryPath.replace(path)


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Normalises audio files to a common integrated loudness with a flat gain.",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="Run with --dry-run first; it measures everything and prints the gains without touching a file.",
    )
    parser.add_argument("paths", nargs="+", type=Path, help="files, or directories of audio files, to normalise")
    parser.add_argument(
        "--target", type=float,
        help="target integrated loudness in LUFS, e.g. -26.0 (mutually exclusive with --reference)",
    )
    parser.add_argument(
        "--reference", type=Path,
        help="a file whose measured loudness becomes the target - use one that already sounds right",
    )
    parser.add_argument(
        "--exclude", action="append", default=[], metavar="NAME",
        help="filename to leave alone, repeatable (e.g. --exclude medsci2.ogg)",
    )
    parser.add_argument(
        "--tolerance", type=float, default=0.3, metavar="LU",
        help="skip files already within this many LU of the target, avoiding a pointless re-encode (default: 0.3)",
    )
    parser.add_argument("--dry-run", action="store_true", help="measure and report, changing nothing")
    arguments = parser.parse_args()

    for executable in ("ffmpeg", "ffprobe"):
        if shutil.which(executable) is None:
            sys.exit(f"{executable} not found on PATH")

    if (arguments.target is None) == (arguments.reference is None):
        sys.exit("pass exactly one of --target or --reference")

    if arguments.reference is not None:
        if not arguments.reference.is_file():
            sys.exit(f"no such reference file: {arguments.reference}")

        target, _ = measure(arguments.reference)
        print(f"reference {arguments.reference.name}: {target:.1f} LUFS\n")
    else:
        target = arguments.target

    files = collect_files(arguments.paths, set(arguments.exclude))
    if not files:
        sys.exit("no audio files matched")

    print(f"target: {target:.1f} LUFS{'  (dry run)' if arguments.dry_run else ''}\n")
    for file in files:
        loudness, peak = measure(file)
        gain = target - loudness
        summary = f"{file.name:<24} {loudness:>7.1f} LUFS  peak {peak:>6.1f} dBFS  ->  {gain:+.2f} dB"

        if abs(gain) <= arguments.tolerance:
            print(f"{summary}   skipped, already on target")
            continue

        if arguments.dry_run:
            print(summary)
            continue

        normalize(file, gain)
        newLoudness, newPeak = measure(file)
        print(f"{summary}   now {newLoudness:.1f} LUFS, peak {newPeak:.1f} dBFS")


if __name__ == "__main__":
    main()
