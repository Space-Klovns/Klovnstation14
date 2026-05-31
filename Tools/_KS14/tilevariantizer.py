from pathlib import Path
from PIL import Image
import numpy as np
import random
import math

INPUT_DIR = Path(input("Enter input folder:"))
OUTPUT_DIR = Path(input("Enter output folder:"))

TILE_SIZE = 32
VARIANTS = 8

def add_blotches(arr):
    h, w = arr.shape[:2]

    for _ in range(random.randint(2, 5)):
        cx = random.uniform(0, w)
        cy = random.uniform(0, h)

        radius = random.uniform(2.5, 8.0)
        strength = random.uniform(-10, 10)

        y_min = max(0, int(cy - radius))
        y_max = min(h, int(cy + radius + 1))

        x_min = max(0, int(cx - radius))
        x_max = min(w, int(cx + radius + 1))

        for y in range(y_min, y_max):
            for x in range(x_min, x_max):
                if arr[y, x, 3] == 0:
                    continue

                dx = x - cx
                dy = y - cy

                dist = math.sqrt(dx * dx + dy * dy)

                if dist > radius:
                    continue

                influence = 1.0 - dist / radius

                arr[y, x, :3] = np.clip(
                    arr[y, x, :3] + strength * influence,
                    0,
                    255
                )


def add_pixel_noise(arr):
    h, w = arr.shape[:2]

    for _ in range(random.randint(15, 40)):
        x = random.randrange(w)
        y = random.randrange(h)

        if arr[y, x, 3] == 0:
            continue

        delta = random.randint(-8, 8)

        arr[y, x, :3] = np.clip(
            arr[y, x, :3] + delta,
            0,
            255
        )


def add_scratches(arr):
    h, w = arr.shape[:2]

    for _ in range(random.randint(1, 3)):
        x = random.randrange(w)
        y = random.randrange(h)

        length = random.randint(3, 10)

        dx = random.choice((-1, 0, 1))
        dy = random.choice((-1, 0, 1))

        if dx == 0 and dy == 0:
            dx = 1

        strength = random.uniform(-12, 12)

        for i in range(length):
            px = x + dx * i
            py = y + dy * i

            if not (0 <= px < w and 0 <= py < h):
                break

            if arr[py, px, 3] == 0:
                continue

            arr[py, px, :3] = np.clip(
                arr[py, px, :3] + strength,
                0,
                255
            )


def make_variant(base):
    arr = np.array(base, dtype=np.float32)

    add_blotches(arr)
    add_pixel_noise(arr)
    add_scratches(arr)

    return Image.fromarray(arr.astype(np.uint8), "RGBA")


def process_file(path: Path):
    image = Image.open(path).convert("RGBA")

    if image.size != (TILE_SIZE, TILE_SIZE):
        print(f"Skipping {path.name}: expected 32x32, got {image.size}")
        return

    sheet = Image.new(
        "RGBA",
        (TILE_SIZE * VARIANTS, TILE_SIZE)
    )

    for i in range(VARIANTS):
        variant = image.copy() if i == 0 else make_variant(image)

        sheet.paste(
            variant,
            (i * TILE_SIZE, 0)
        )

    output_path = OUTPUT_DIR / path.name
    sheet.save(output_path)

    print(f"Processed {path.name}")


def main():
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)

    for file in INPUT_DIR.iterdir():
        if file.suffix.lower() != ".png":
            continue

        process_file(file)


if __name__ == "__main__":
    random.seed()
    main()
