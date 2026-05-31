import argparse
from pathlib import Path
from PIL import Image
import numpy as np
import random

TILE_SIZE = 32
VARIANTS = 8


# -----------------------------
# subtle luminance noise only
# -----------------------------
def add_micro_noise(arr, strength=7.0):
    h, w = arr.shape[:2]

    noise = np.random.randn(h, w) * strength

    # apply equally to RGB (no chroma shift)
    for c in range(3):
        arr[:, :, c] += noise

    np.clip(arr, 0, 255, out=arr)


# -----------------------------
# very mild "compression drift"
# (block quantization simulation)
# -----------------------------
def block_quantize(arr, block=3, intensity=0.1):
    h, w = arr.shape[:2]

    for by in range(0, h, block):
        for bx in range(0, w, block):
            patch = arr[by:by+block, bx:bx+block, :3]

            mean = patch.reshape(-1, 3).mean(axis=0)

            # blend toward block mean slightly
            patch[:] = patch * (1.0 - intensity) + mean * intensity


# -----------------------------
# tiny pixel jitter (subtle)
# -----------------------------
def micro_shift(arr):
    h, w = arr.shape[:2]

    shifted = arr.copy()

    for y in range(h):
        for x in range(w):
            if arr[y, x, 3] == 0:
                continue

            dx = random.choice([-1, 0, 1])
            dy = random.choice([-1, 0, 1])

            nx = min(max(x + dx, 0), w - 1)
            ny = min(max(y + dy, 0), h - 1)

            shifted[y, x] = arr[ny, nx]

    return shifted


# -----------------------------
# variant generator
# -----------------------------
def make_variant(base):
    arr = np.array(base).astype(np.float32)

    # subtle structure-first jitter
    # if random.random() < 0.5:
    #     arr = micro_shift(arr)

    # very light compression-like smoothing
    block_quantize(arr, block=5, intensity=0.11)

    # very subtle luminance noise (main effect)
    add_micro_noise(arr, strength=1.65)

    np.clip(arr, 0, 255, out=arr)

    return Image.fromarray(arr.astype(np.uint8), "RGBA")


# -----------------------------
# processing
# -----------------------------
def process_file(path: Path, out_dir: Path):
    img = Image.open(path).convert("RGBA")

    if img.size != (TILE_SIZE, TILE_SIZE):
        print(f"Skipping {path.name}: not 32x32")
        return

    sheet = Image.new("RGBA", (TILE_SIZE * VARIANTS, TILE_SIZE))

    for i in range(VARIANTS):
        if i == 0:
            variant = img.copy()
        else:
            variant = make_variant(img)

        sheet.paste(variant, (i * TILE_SIZE, 0))

    out_path = out_dir / path.name
    sheet.save(out_path)

    print(f"Processed {path.name}")


# -----------------------------
# CLI
# -----------------------------
def main():
    parser = argparse.ArgumentParser()

    parser.add_argument("--input", required=True, help="Input folder")
    parser.add_argument("--output", required=True, help="Output folder")

    args = parser.parse_args()

    in_dir = Path(args.input)
    out_dir = Path(args.output)

    out_dir.mkdir(parents=True, exist_ok=True)

    for file in in_dir.iterdir():
        if file.suffix.lower() != ".png":
            continue

        process_file(file, out_dir)


if __name__ == "__main__":
    random.seed()
    np.random.seed()
    main()
