#!/usr/bin/env python3
# KS14: added in this fork
"""
Generates heaven.rsi: the effects of heaven itself answering a cultivator.

- bolt_0..2: a bolt of heavenly lightning striking down onto a tile: a stepped leader feeling its way down, the main
  stroke with its branches, a re-strike and an afterglow. Each variant waits a little longer before it strikes, so three
  spawned together fall one after another. The strike point is 16 pixels above the bottom of the canvas.
- strike_flash: the ground where it lands: a white-hot burst, arcs crawling outward over the floor and a scorch ring.
- crackle: arcs of lightning crawling over a body; loops.
- ascend_pillar: the golden column of a successful breakthrough, rising from the ground into the sky, shimmering with
  streaks and motes drifting up it, then thinning away.

Everything is drawn in its own colours, with a soft outer glow, a brighter inner glow and a white-hot core, so it reads
as light rather than paint.

    python3 Tools/_KS14/cultivation_heaven.py

Needs Pillow.
"""

import json
import math
import os
import random

from PIL import Image, ImageChops, ImageDraw, ImageFilter

OUT = "Resources/Textures/_KS14/Cultivation/heaven.rsi"

BOLT_W, BOLT_H = 96, 352
FLASH = 128
CRACKLE = 64
PILLAR_W, PILLAR_H = 96, 320

GLOW = (120, 150, 255)
INNER = (190, 210, 255)
CORE = (255, 255, 255)


def blank(width, height):
    return Image.new("RGBA", (width, height), (0, 0, 0, 0))


def add(base, layer):
    """Light adds up: a brighter layer over a dimmer one, alpha and colour together."""
    return ImageChops.add(base, layer)


def tinted(mask, colour, alpha_scale=1.0):
    """A solid colour, shaped by a greyscale mask."""
    layer = Image.new("RGBA", mask.size, colour + (0,))
    layer.putalpha(mask.point(lambda value: int(min(255, value * alpha_scale))))
    return layer


def light(mask, glow=GLOW, inner=INNER, core=CORE, spread=7, strength=1.0):
    """Turn a hard-edged mask of where the light is into glowing light: halo, inner glow, white core."""
    halo = tinted(mask.filter(ImageFilter.GaussianBlur(spread)), glow, 2.6 * strength)
    middle = tinted(mask.filter(ImageFilter.GaussianBlur(max(1, spread / 3))), inner, 1.8 * strength)
    centre = tinted(mask, core, strength)
    frame = Image.alpha_composite(halo, middle)
    return Image.alpha_composite(frame, centre)


def jagged(rng, start, end, roughness, depth):
    """A jagged line from start to end, by repeatedly displacing midpoints sideways."""
    points = [start, end]
    for level in range(depth):
        refined = [points[0]]
        for (x1, y1), (x2, y2) in zip(points, points[1:]):
            length = math.hypot(x2 - x1, y2 - y1)
            offset = rng.uniform(-1, 1) * roughness * length / (1.6 ** level) * 0.5
            nx, ny = -(y2 - y1) / (length or 1), (x2 - x1) / (length or 1)
            refined += [((x1 + x2) / 2 + nx * offset, (y1 + y2) / 2 + ny * offset), (x2, y2)]
        points = refined
    return points


def bolt_shape(seed, width, height, ground):
    """The main stroke and its branches, as lists of points with a thickness each."""
    rng = random.Random(seed)
    top = (width / 2 + rng.uniform(-18, 18), 0)
    bottom = (width / 2, ground)
    trunk = jagged(rng, top, bottom, 0.55, 6)
    strokes = [(trunk, 3)]
    for _ in range(rng.randint(4, 6)):
        index = rng.randint(4, len(trunk) * 3 // 4)
        x, y = trunk[index]
        reach = rng.uniform(40, 110)
        angle = math.radians(rng.choice((-1, 1)) * rng.uniform(25, 60))
        end = (x + math.sin(angle) * reach, y + math.cos(angle) * reach)
        branch = jagged(rng, (x, y), end, 0.6, 4)
        strokes.append((branch, 1))
        # Twigs off the larger branches.
        if rng.random() < 0.6:
            twig_index = rng.randint(1, len(branch) - 2)
            tx, ty = branch[twig_index]
            twig_end = (tx + rng.uniform(-24, 24), ty + rng.uniform(10, 30))
            strokes.append((jagged(rng, (tx, ty), twig_end, 0.7, 3), 1))
    return trunk, strokes


def draw_strokes(size, strokes, cut=None, thicken=0):
    """Draw strokes into a mask, optionally only down to a height (the stepped leader still feeling its way down)."""
    mask = Image.new("L", size, 0)
    draw = ImageDraw.Draw(mask)
    for points, width in strokes:
        visible = [point for point in points if cut is None or point[1] <= cut]
        if len(visible) >= 2:
            draw.line(visible, fill=255, width=width + thicken, joint="curve")
    return mask


def bolt(seed, delay_frames):
    """One heavenly bolt. Returns (frames, delays)."""
    ground = BOLT_H - 16
    trunk, strokes = bolt_shape(seed, BOLT_W, BOLT_H, ground)
    frames, delays = [], []
    for _ in range(delay_frames):
        frames.append(blank(BOLT_W, BOLT_H))
        delays.append(0.05)

    # The stepped leader: faint, thin, feeling its way down in jerks.
    for fraction in (0.3, 0.65):
        mask = draw_strokes((BOLT_W, BOLT_H), strokes[:3], cut=ground * fraction)
        frames.append(light(mask, spread=4, strength=0.55))
        delays.append(0.03)

    # The return stroke: the whole channel lit at once, blinding.
    full = draw_strokes((BOLT_W, BOLT_H), strokes, thicken=1)
    frame = light(full, spread=9, strength=1.1)
    sky = Image.new("L", (BOLT_W, BOLT_H), 0)
    ImageDraw.Draw(sky).ellipse((BOLT_W / 2 - 40, ground - 26, BOLT_W / 2 + 40, ground + 14), fill=140)
    frames.append(add(frame, tinted(sky.filter(ImageFilter.GaussianBlur(10)), (210, 225, 255), 1.0)))
    delays.append(0.06)

    frames.append(light(full, spread=7, strength=1.0))
    delays.append(0.04)

    # It dims, then the channel flashes again.
    frames.append(light(draw_strokes((BOLT_W, BOLT_H), strokes[:2]), spread=5, strength=0.45))
    delays.append(0.04)
    frames.append(light(draw_strokes((BOLT_W, BOLT_H), strokes, thicken=1), spread=8, strength=1.2))
    delays.append(0.05)

    # Afterglow: only the trunk, fading, then nothing.
    for strength in (0.6, 0.3, 0.12):
        frames.append(light(draw_strokes((BOLT_W, BOLT_H), strokes[:1]), spread=6, strength=strength))
        delays.append(0.06)
    frames.append(blank(BOLT_W, BOLT_H))
    delays.append(0.05)
    return frames, delays


def strike_flash(frames=9, seed=5):
    """The floor where the bolt lands."""
    rng = random.Random(seed)
    centre = FLASH / 2
    arcs = []
    for index in range(9):
        angle = index / 9 * math.tau + rng.uniform(-0.3, 0.3)
        reach = rng.uniform(36, 58)
        end = (centre + math.cos(angle) * reach, centre + math.sin(angle) * reach * 0.7)
        arcs.append(jagged(rng, (centre, centre), end, 0.6, 4))
    result = []
    for index in range(frames):
        t = index / (frames - 1)
        frame = blank(FLASH, FLASH)
        # The white-hot burst, gone almost at once.
        if t < 0.45:
            burst = Image.new("L", (FLASH, FLASH), 0)
            radius = 10 + 34 * t
            ImageDraw.Draw(burst).ellipse((centre - radius, centre - radius * 0.7, centre + radius, centre + radius * 0.7),
                                          fill=int(255 * (1 - t / 0.45)))
            frame = add(frame, light(burst, spread=8, strength=1.0))
        # Arcs crawling out over the floor, reaching further, flickering.
        mask = Image.new("L", (FLASH, FLASH), 0)
        draw = ImageDraw.Draw(mask)
        for arc in arcs:
            if rng.random() < 0.75 - 0.5 * t:
                length = max(2, int(len(arc) * min(1.0, 0.4 + t * 1.4)))
                draw.line(arc[:length], fill=int(255 * (1 - t * 0.8)), width=1)
        frame = add(frame, light(mask, spread=3, strength=1.1))
        # The scorch ring left behind.
        ring = Image.new("L", (FLASH, FLASH), 0)
        r = 14 + 6 * t
        ImageDraw.Draw(ring).ellipse((centre - r, centre - r * 0.6, centre + r, centre + r * 0.6), outline=255, width=3)
        scorch = Image.new("RGBA", (FLASH, FLASH), (40, 30, 60, 0))
        scorch.putalpha(ring.filter(ImageFilter.GaussianBlur(2)).point(lambda value: int(value * 0.55 * (1 - t ** 2))))
        result.append(Image.alpha_composite(scorch, frame))
    return result


def crackle(frames=10, seed=11):
    """Arcs of lightning crawling over a standing figure; loops."""
    rng = random.Random(seed)
    result = []
    for index in range(frames):
        mask = Image.new("L", (CRACKLE, CRACKLE), 0)
        draw = ImageDraw.Draw(mask)
        for _ in range(rng.randint(2, 4)):
            # Somewhere on the figure's outline (a capsule centred on the body) to somewhere else near it.
            a = rng.uniform(0, math.tau)
            b = a + rng.uniform(0.5, 1.6) * rng.choice((-1, 1))
            start = (32 + math.cos(a) * 11, 36 + math.sin(a) * 17)
            end = (32 + math.cos(b) * 13, 36 + math.sin(b) * 19)
            draw.line(jagged(rng, start, end, 0.7, 3), fill=rng.randint(170, 255), width=1)
        frame = light(mask, spread=3, strength=1.1)
        # A faint flicker around the whole body.
        glow = Image.new("L", (CRACKLE, CRACKLE), 0)
        ImageDraw.Draw(glow).ellipse((20, 16, 44, 56), fill=rng.randint(20, 45))
        result.append(add(tinted(glow.filter(ImageFilter.GaussianBlur(6)), GLOW, 1.0), frame))
    return result


def ascend_pillar(frames=16, seed=3):
    """A column of golden light from the ground to the heavens."""
    rng = random.Random(seed)
    ground = PILLAR_H - 16
    centre = PILLAR_W / 2
    motes = [(rng.uniform(-14, 14), rng.uniform(0, 1), rng.uniform(1, 2.2)) for _ in range(26)]
    streaks = [(rng.uniform(-9, 9), rng.uniform(0, 1), rng.uniform(30, 80)) for _ in range(10)]
    result = []
    for index in range(frames):
        t = index / (frames - 1)
        rise = min(1.0, t / 0.22)
        fade = 1.0 if t < 0.65 else max(0.0, (1 - t) / 0.35)
        width = (10 + 6 * math.sin(t * math.pi)) * (0.4 + 0.6 * fade)
        top = ground - rise * ground
        column = Image.new("L", (PILLAR_W, PILLAR_H), 0)
        draw = ImageDraw.Draw(column)
        for layer in range(int(width), 0, -1):
            value = int(255 * fade * (1 - layer / (width + 1)) ** 0.8)
            draw.rectangle((centre - layer, top, centre + layer, ground), fill=value)
        # Brightest where it leaves the ground, flaring into a pool of light.
        draw.ellipse((centre - width * 2.4, ground - 9, centre + width * 2.4, ground + 9), fill=int(220 * fade))
        frame = light(column, glow=(255, 150, 20), inner=(255, 205, 90), core=(255, 248, 215), spread=10, strength=0.85)
        # Bright streaks racing up the column.
        mask = Image.new("L", (PILLAR_W, PILLAR_H), 0)
        sd = ImageDraw.Draw(mask)
        for x, phase, length in streaks:
            p = (phase + t * 2.2) % 1.0
            y = ground - p * ground
            if y > top:
                sd.line((centre + x, y, centre + x, max(top, y - length)), fill=int(230 * fade), width=1)
        # Motes drifting up around it.
        for x, phase, size in motes:
            p = (phase + t * 1.3) % 1.0
            y = ground - p * ground * 0.85
            if y > top:
                mx = centre + x * (1.2 + p) + math.sin(p * 9 + phase * 6) * 3
                sd.ellipse((mx - size, y - size, mx + size, y + size), fill=int(255 * fade * math.sin(p * math.pi)))
        frame = add(frame, light(mask, glow=(255, 190, 60), inner=(255, 230, 150), core=(255, 255, 240), spread=3, strength=1.0))
        result.append(frame)
    return result


def save(name, frames, delays, width, height, meta_states):
    image = Image.new("RGBA", (width * len(frames), height), (0, 0, 0, 0))
    for index, frame in enumerate(frames):
        image.paste(frame, (width * index, 0))
    image.save(os.path.join(OUT, name + ".png"))
    meta_states.append({"name": name, "delays": [delays]})


def main():
    os.makedirs(OUT, exist_ok=True)
    # The RSI has one frame size, so the smaller effects are drawn in the middle of the largest canvas.
    width, height = BOLT_W, BOLT_H
    states = []

    def fit(frames, frame_width, frame_height, bottom=False):
        placed = []
        for frame in frames:
            canvas = blank(width, height)
            y = height - frame_height if bottom else (height - frame_height) // 2
            canvas.paste(frame, ((width - frame_width) // 2, y))
            placed.append(canvas)
        return placed

    for variant, delay_frames in enumerate((0, 3, 6)):
        frames, delays = bolt(100 + variant * 17, delay_frames)
        save(f"bolt_{variant}", frames, delays, width, height, states)

    flash = [frame.crop((16, 0, 112, 128)) for frame in strike_flash()]
    save("strike_flash", fit(flash, 96, 128), [0.06] * len(flash), width, height, states)
    save("crackle", fit(crackle(), CRACKLE, CRACKLE), [0.07] * 10, width, height, states)
    save("ascend_pillar", fit(ascend_pillar(), PILLAR_W, PILLAR_H, bottom=True), [0.07] * 16, width, height, states)

    meta = {
        "version": 1,
        "license": "CC-BY-SA-3.0",
        "copyright": "Generated for Klovnstation 14 by Tools/_KS14/cultivation_heaven.py",
        "size": {"x": width, "y": height},
        "states": states,
    }
    with open(os.path.join(OUT, "meta.json"), "w", encoding="utf-8") as file:
        json.dump(meta, file, indent=2)
        file.write("\n")


if __name__ == "__main__":
    main()
