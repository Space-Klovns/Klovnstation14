#!/usr/bin/env python3
# KS14: added in this fork
"""
Generates the animated cultivation effects RSI: an expanding qi wave, a hit spark, rising qi motes and a pillar of
light. Everything is drawn white so prototypes can tint it per element with the sprite's colour.

    python3 Tools/_KS14/cultivation_effects.py

Needs Pillow.
"""

import json
import math
import os
import random

from PIL import Image, ImageDraw, ImageFilter

OUT = "Resources/Textures/_KS14/Cultivation/qi_effects.rsi"
SIZE = 64


def sheet(frames):
    """RSI states store their frames left to right."""
    image = Image.new("RGBA", (SIZE * len(frames), SIZE), (0, 0, 0, 0))
    for index, frame in enumerate(frames):
        image.paste(frame, (SIZE * index, 0))
    return image


def wave(frames=8):
    """A ring of qi bursting outward and thinning as it goes."""
    result = []
    for index in range(frames):
        t = (index + 1) / frames
        radius = 4 + 27 * t
        width = max(1, int(5 * (1 - t)) + 1)
        alpha = int(255 * (1 - t) ** 0.6)
        frame = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
        draw = ImageDraw.Draw(frame)
        c = SIZE / 2
        draw.ellipse((c - radius, c - radius, c + radius, c + radius), outline=(255, 255, 255, alpha), width=width)
        glow = frame.filter(ImageFilter.GaussianBlur(1.5))
        frame = Image.alpha_composite(glow, frame)
        result.append(frame)
    return result


def spark(frames=5, seed=3):
    """A star of light where a technique strikes."""
    rng = random.Random(seed)
    rays = [(rng.uniform(0, 2 * math.pi), rng.uniform(0.6, 1.0)) for _ in range(8)]
    result = []
    for index in range(frames):
        t = (index + 1) / frames
        frame = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
        draw = ImageDraw.Draw(frame)
        c = SIZE / 2
        alpha = int(255 * (1 - t * 0.8))
        reach = 6 + 16 * math.sin(t * math.pi / 2)
        for angle, length in rays:
            end = (c + math.cos(angle) * reach * length, c + math.sin(angle) * reach * length)
            draw.line((c, c, end[0], end[1]), fill=(255, 255, 255, alpha), width=2 if index < 2 else 1)
        core = 5 * (1 - t) + 1
        draw.ellipse((c - core, c - core, c + core, c + core), fill=(255, 255, 255, alpha))
        result.append(Image.alpha_composite(frame.filter(ImageFilter.GaussianBlur(1)), frame))
    return result


def motes(frames=10, seed=11):
    """Specks of qi drifting up around a cultivator; loops."""
    rng = random.Random(seed)
    specks = [(rng.uniform(14, 50), rng.uniform(0, 1), rng.uniform(1, 2.2), rng.uniform(-0.15, 0.15)) for _ in range(14)]
    result = []
    for index in range(frames):
        frame = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
        draw = ImageDraw.Draw(frame)
        for x, phase, size, sway in specks:
            p = (phase + index / frames) % 1.0
            y = 58 - p * 50
            px = x + math.sin(p * 6.28) * 3 + sway * p * 20
            alpha = int(255 * math.sin(p * math.pi))
            draw.ellipse((px - size, y - size, px + size, y + size), fill=(255, 255, 255, alpha))
        result.append(Image.alpha_composite(frame.filter(ImageFilter.GaussianBlur(0.8)), frame))
    return result


def pillar(frames=8):
    """A column of light rising from the ground and fading."""
    result = []
    for index in range(frames):
        t = (index + 1) / frames
        frame = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
        draw = ImageDraw.Draw(frame)
        grow = min(1.0, t * 2.5)
        fade = 1.0 if t < 0.6 else (1 - t) / 0.4
        width = 10 * (1 - abs(0.5 - t))
        top = SIZE - grow * SIZE
        for column in range(int(width), 0, -1):
            alpha = int(200 * fade * (1 - column / (width + 1)))
            draw.rectangle((SIZE / 2 - column, top, SIZE / 2 + column, SIZE), fill=(255, 255, 255, alpha))
        result.append(frame.filter(ImageFilter.GaussianBlur(1.2)))
    return result


def aura(frames=12, seed=23):
    """Qi blazing off a body like a white flame: tongues of light licking up the figure's outline, a brighter rim
    hugging it, a ring of light pooled round the feet and sparks rising off the top; loops seamlessly."""
    rng = random.Random(seed)
    # Flame tongues spread round the outline: (angle round the body, phase, height, width).
    tongues = [(rng.uniform(-1.9, 1.9), rng.uniform(0, 1), rng.uniform(10, 20), rng.uniform(2.5, 4.5)) for _ in range(22)]
    sparks = [(rng.uniform(22, 42), rng.uniform(0, 1), rng.uniform(0.8, 1.5)) for _ in range(8)]
    result = []
    for index in range(frames):
        t = index / frames
        breath = 0.94 + 0.06 * math.sin(t * 2 * math.pi)
        soft = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
        draw = ImageDraw.Draw(soft)
        # The glow hugging the figure (a capsule the size of a person, feet at y=52).
        for layer in range(5, 0, -1):
            width, height = (9 + layer * 1.5) * breath, (15 + layer * 1.6) * breath
            draw.ellipse((32 - width, 36 - height, 32 + width, 36 + height), fill=(255, 255, 255, 10 + (5 - layer) * 8))
        # The ring of qi pooled round the feet.
        for layer in range(3, 0, -1):
            draw.ellipse((32 - 12 - layer * 2, 50 - 3 - layer, 32 + 12 + layer * 2, 50 + 3 + layer), outline=(255, 255, 255, 30 + 22 * (3 - layer)), width=1)
        # Flame tongues rising off the outline and curling in toward the head as they climb.
        for angle, phase, height, width in tongues:
            p = (phase + t) % 1.0
            base_x = 32 + math.sin(angle) * 12
            base_y = 40 - math.cos(angle) * 8
            rise = p * height
            x = base_x + math.sin((p + phase) * 6.28) * 1.5 - math.sin(angle) * rise * 0.35
            y = base_y - rise
            w = width * (1 - p * 0.7)
            alpha = int(235 * math.sin(p * math.pi) ** 0.7)
            draw.ellipse((x - w, y - w * 2.6, x + w, y + w * 1.2), fill=(255, 255, 255, alpha // 2))
            draw.ellipse((x - w * 0.45, y - w * 2.0, x + w * 0.45, y + w * 0.6), fill=(255, 255, 255, alpha))
        soft = Image.alpha_composite(soft.filter(ImageFilter.GaussianBlur(2.4)), soft.filter(ImageFilter.GaussianBlur(0.9)))
        # A crisp, brighter rim and sparks on top, so it reads as light and not fog.
        crisp = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
        crisp_draw = ImageDraw.Draw(crisp)
        for x, phase, size in sparks:
            p = (phase + t) % 1.0
            y = 30 - p * 24
            sx = x + math.sin((p + phase) * 9) * 2
            crisp_draw.ellipse((sx - size, y - size, sx + size, y + size), fill=(255, 255, 255, int(230 * math.sin(p * math.pi))))
        result.append(Image.alpha_composite(soft, crisp.filter(ImageFilter.GaussianBlur(0.5))))
    return result


def _glow(frame, blur=1.2):
    return Image.alpha_composite(frame.filter(ImageFilter.GaussianBlur(blur)), frame)


def cast_slash(frames=7):
    """Metal: three crescent blades of light wheeling outward."""
    result = []
    for index in range(frames):
        t = (index + 1) / frames
        frame = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
        draw = ImageDraw.Draw(frame)
        alpha = int(255 * (1 - t) ** 0.5)
        radius = 8 + 20 * t
        for blade in range(3):
            start = blade * 120 + t * 160
            draw.arc((32 - radius, 32 - radius, 32 + radius, 32 + radius), start, start + 70, fill=(255, 255, 255, alpha), width=max(1, int(4 * (1 - t)) + 1))
        result.append(_glow(frame))
    return result


def cast_ripple(frames=9):
    """Water: ring after ring spreading like a stone dropped in a lake."""
    result = []
    for index in range(frames):
        t = (index + 1) / frames
        frame = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
        draw = ImageDraw.Draw(frame)
        for ring in range(3):
            rt = t - ring * 0.22
            if rt <= 0:
                continue
            radius = 3 + 28 * rt
            alpha = int(230 * (1 - rt))
            draw.ellipse((32 - radius, 32 - radius * 0.8, 32 + radius, 32 + radius * 0.8), outline=(255, 255, 255, max(0, alpha)), width=2)
        result.append(_glow(frame, 0.9))
    return result


def cast_flare(frames=7, seed=9):
    """Fire: a ragged burst of flame tongues."""
    rng = random.Random(seed)
    tongues = [(i * 2 * math.pi / 12 + rng.uniform(-0.2, 0.2), rng.uniform(0.6, 1.0)) for i in range(12)]
    result = []
    for index in range(frames):
        t = (index + 1) / frames
        frame = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
        draw = ImageDraw.Draw(frame)
        alpha = int(255 * (1 - t * 0.85))
        reach = 8 + 22 * math.sin(t * math.pi / 2)
        for angle, length in tongues:
            tip = (32 + math.cos(angle) * reach * length, 32 + math.sin(angle) * reach * length)
            left = (32 + math.cos(angle - 0.22) * reach * 0.45, 32 + math.sin(angle - 0.22) * reach * 0.45)
            right = (32 + math.cos(angle + 0.22) * reach * 0.45, 32 + math.sin(angle + 0.22) * reach * 0.45)
            draw.polygon([left, tip, right], fill=(255, 255, 255, alpha))
        core = 9 * (1 - t)
        draw.ellipse((32 - core, 32 - core, 32 + core, 32 + core), fill=(255, 255, 255, alpha))
        result.append(_glow(frame, 1.5))
    return result


def cast_rune(frames=8):
    """Earth: an eight-sided seal stamping outward, heavy and square."""
    result = []
    for index in range(frames):
        t = (index + 1) / frames
        frame = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
        draw = ImageDraw.Draw(frame)
        alpha = int(255 * (1 - t) ** 0.7)
        for scale, width in ((1.0, 3), (0.62, 1)):
            radius = (6 + 24 * t) * scale
            points = [(32 + math.cos(k * math.pi / 4 + math.pi / 8) * radius, 32 + math.sin(k * math.pi / 4 + math.pi / 8) * radius) for k in range(8)]
            draw.line(points + [points[0]], fill=(255, 255, 255, alpha), width=width)
        radius = 6 + 24 * t
        for k in range(4):
            angle = k * math.pi / 2
            draw.line((32 + math.cos(angle) * radius * 0.62, 32 + math.sin(angle) * radius * 0.62,
                       32 + math.cos(angle) * radius, 32 + math.sin(angle) * radius), fill=(255, 255, 255, alpha), width=2)
        result.append(_glow(frame))
    return result


def cast_petals(frames=9):
    """Wood: petals spiralling out and away."""
    result = []
    for index in range(frames):
        t = (index + 1) / frames
        frame = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
        draw = ImageDraw.Draw(frame)
        alpha = int(255 * (1 - t) ** 0.6)
        for petal in range(8):
            angle = petal * math.pi / 4 + t * 2.2
            radius = 5 + 22 * t
            x, y = 32 + math.cos(angle) * radius, 32 + math.sin(angle) * radius
            tip = (x + math.cos(angle) * 6, y + math.sin(angle) * 6)
            side = (math.cos(angle + math.pi / 2) * 2.5, math.sin(angle + math.pi / 2) * 2.5)
            draw.polygon([(x - side[0], y - side[1]), tip, (x + side[0], y + side[1]), (x - math.cos(angle) * 3, y - math.sin(angle) * 3)],
                         fill=(255, 255, 255, alpha))
        result.append(_glow(frame, 0.8))
    return result


def cast_spikes(frames=6, seed=31):
    """Blood: thin jagged thorns stabbing out all at once."""
    rng = random.Random(seed)
    thorns = [(rng.uniform(0, 2 * math.pi), rng.uniform(0.5, 1.0)) for _ in range(18)]
    result = []
    for index in range(frames):
        t = (index + 1) / frames
        frame = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
        draw = ImageDraw.Draw(frame)
        alpha = int(255 * (1 - t * 0.7))
        reach = 30 * min(1.0, t * 2.2)
        for angle, length in thorns:
            draw.line((32 + math.cos(angle) * 5, 32 + math.sin(angle) * 5, 32 + math.cos(angle) * reach * length, 32 + math.sin(angle) * reach * length),
                      fill=(255, 255, 255, alpha), width=1)
        draw.ellipse((27, 27, 37, 37), fill=(255, 255, 255, int(alpha * 0.8)))
        result.append(_glow(frame, 0.7))
    return result


def cast_implode(frames=8):
    """Void: light falling inward to a point, then gone."""
    result = []
    for index in range(frames):
        t = (index + 1) / frames
        frame = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
        draw = ImageDraw.Draw(frame)
        radius = 30 * (1 - t) + 2
        alpha = int(120 + 135 * t)
        draw.ellipse((32 - radius, 32 - radius, 32 + radius, 32 + radius), outline=(255, 255, 255, alpha), width=2)
        for k in range(6):
            angle = k * math.pi / 3 + t * 1.5
            draw.line((32 + math.cos(angle) * radius, 32 + math.sin(angle) * radius, 32 + math.cos(angle) * radius * 0.5, 32 + math.sin(angle) * radius * 0.5),
                      fill=(255, 255, 255, alpha))
        result.append(_glow(frame))
    return result


def cast_rays(frames=8):
    """Gold: a sunburst of long straight rays."""
    result = []
    for index in range(frames):
        t = (index + 1) / frames
        frame = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
        draw = ImageDraw.Draw(frame)
        alpha = int(255 * (1 - t) ** 0.6)
        inner, outer = 4 + 10 * t, 10 + 22 * t
        for k in range(16):
            angle = k * math.pi / 8
            reach = outer if k % 2 == 0 else outer * 0.7
            draw.line((32 + math.cos(angle) * inner, 32 + math.sin(angle) * inner, 32 + math.cos(angle) * reach, 32 + math.sin(angle) * reach),
                      fill=(255, 255, 255, alpha), width=2 if k % 2 == 0 else 1)
        draw.ellipse((32 - inner, 32 - inner, 32 + inner, 32 + inner), outline=(255, 255, 255, alpha), width=1)
        result.append(_glow(frame))
    return result


def dome(frames=8):
    """A bell of light closed over the body; shimmers; loops."""
    result = []
    for index in range(frames):
        t = index / frames
        frame = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
        draw = ImageDraw.Draw(frame)
        shimmer = 0.8 + 0.2 * math.sin(t * 2 * math.pi)
        draw.ellipse((10, 6, 54, 62), fill=(255, 255, 255, int(40 * shimmer)), outline=(255, 255, 255, int(200 * shimmer)), width=2)
        # Bands round the bell, sliding down.
        for band in range(3):
            y = 14 + ((band * 16 + t * 16) % 46)
            half = math.sqrt(max(0.0, 1 - ((y - 34) / 28) ** 2)) * 22
            draw.line((32 - half, y, 32 + half, y), fill=(255, 255, 255, int(110 * shimmer)), width=1)
        result.append(Image.alpha_composite(frame.filter(ImageFilter.GaussianBlur(1)), frame))
    return result


def sigil(frames=12):
    """A circle of trigram ticks turning slowly under the feet; loops."""
    result = []
    for index in range(frames):
        turn = index / frames * (2 * math.pi / 8)
        frame = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
        draw = ImageDraw.Draw(frame)
        c, outer, inner = SIZE / 2, 28, 21
        draw.ellipse((c - outer, c - outer, c + outer, c + outer), outline=(255, 255, 255, 190), width=2)
        draw.ellipse((c - inner, c - inner, c + inner, c + inner), outline=(255, 255, 255, 140), width=1)
        for tick in range(8):
            angle = turn + tick * 2 * math.pi / 8
            for bar in range(3):
                r = inner + 1.5 + bar * 2
                span = 0.22 if (tick + bar) % 2 == 0 else 0.09
                for side in (-1, 1):
                    a0 = angle + side * 0.04
                    a1 = angle + side * span
                    draw.line((c + math.cos(a0) * r, c + math.sin(a0) * r, c + math.cos(a1) * r, c + math.sin(a1) * r),
                              fill=(255, 255, 255, 210))
        result.append(Image.alpha_composite(frame.filter(ImageFilter.GaussianBlur(1)), frame))
    return result


def big_sheet(frames):
    image = Image.new("RGBA", (BIG * len(frames), BIG), (0, 0, 0, 0))
    for index, frame in enumerate(frames):
        image.paste(frame, (BIG * index, 0))
    return image


def _palm(scale, alpha):
    """An open golden hand seen from below, fingers up, a dharma wheel in the palm."""
    image = Image.new("RGBA", (BIG, BIG), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    gold, deep, light = (255, 208, 84, alpha), (176, 112, 22, alpha), (255, 240, 170, alpha)
    c = BIG / 2

    def box(x0, y0, x1, y1, radius, fill, outline):
        draw.rounded_rectangle((c + x0 * scale, c + y0 * scale, c + x1 * scale, c + y1 * scale), radius=max(1, radius * scale), fill=fill, outline=outline, width=max(1, int(2 * scale)))

    # Fingers, then thumb, then the palm over their roots.
    for x, top in ((-24, -30), (-11, -40), (2, -42), (15, -34)):
        box(x, top, x + 11, 4, 5, gold, deep)
        for knuckle in (0.38, 0.68):
            y = top + (4 - top) * knuckle
            draw.line((c + (x + 2) * scale, c + y * scale, c + (x + 9) * scale, c + y * scale), fill=deep, width=1)
    draw.polygon([(c + 24 * scale, c + 2 * scale), (c + 40 * scale, c - 14 * scale), (c + 44 * scale, c - 6 * scale), (c + 30 * scale, c + 22 * scale)], fill=gold, outline=deep)
    box(-27, -4, 29, 40, 12, gold, deep)
    draw.arc((c - 20 * scale, c + 2 * scale, c + 22 * scale, c + 34 * scale), 200, 340, fill=light, width=max(1, int(2 * scale)))
    # The wheel.
    radius = 11 * scale
    draw.ellipse((c - radius, c + 16 * scale - radius, c + radius, c + 16 * scale + radius), outline=deep, width=max(1, int(2 * scale)))
    for spoke in range(8):
        angle = spoke * math.pi / 4
        draw.line((c, c + 16 * scale, c + math.cos(angle) * radius, c + 16 * scale + math.sin(angle) * radius), fill=deep, width=1)
    draw.ellipse((c - 3 * scale, c + 13 * scale, c + 3 * scale, c + 19 * scale), fill=light)
    return image


# Signature animations are drawn on a three-tile canvas.
BIG = 96


def falling_rock(frames=9, seed=41):
    """A boulder dropping out of the air and breaking apart where it lands."""
    rng = random.Random(seed)
    shape = [(math.cos(a) * (15 + rng.uniform(-3, 3)), math.sin(a) * (13 + rng.uniform(-3, 3))) for a in [i * 2 * math.pi / 9 for i in range(9)]]
    shards = [(rng.uniform(0, 2 * math.pi), rng.uniform(10, 34), rng.uniform(2, 5)) for _ in range(12)]
    result = []
    for index in range(frames):
        t = (index + 1) / frames
        frame = Image.new("RGBA", (BIG, BIG), (0, 0, 0, 0))
        draw = ImageDraw.Draw(frame)
        c = BIG / 2
        if t <= 0.6:
            fall = t / 0.6
            y = c - 44 * (1 - fall) ** 1.5
            shadow = 6 + 12 * fall
            draw.ellipse((c - shadow, c + 12 - shadow * 0.35, c + shadow, c + 12 + shadow * 0.35), fill=(0, 0, 0, int(120 * fall)))
            points = [(c + x, y + py) for x, py in shape]
            draw.polygon(points, fill=(122, 104, 84, 255), outline=(60, 48, 38, 255))
            draw.polygon([(c + x * 0.55 - 3, y + py * 0.55 - 3) for x, py in shape], fill=(160, 140, 114, 255))
        else:
            burst = (t - 0.6) / 0.4
            for angle, reach, size in shards:
                x, y = c + math.cos(angle) * reach * burst, c + 6 + math.sin(angle) * reach * burst * 0.7
                draw.rectangle((x - size, y - size, x + size, y + size), fill=(122, 104, 84, int(255 * (1 - burst))), outline=(60, 48, 38, int(255 * (1 - burst))))
            dust = 14 + 26 * burst
            draw.ellipse((c - dust, c + 8 - dust * 0.5, c + dust, c + 8 + dust * 0.5), outline=(190, 174, 150, int(200 * (1 - burst))), width=3)
        result.append(frame)
    return result


def tide(frames=10):
    """A wall of water rolling outward from the caster: a foaming crest with the dark swell behind it."""
    result = []
    for index in range(frames):
        t = (index + 1) / frames
        frame = Image.new("RGBA", (BIG, BIG), (0, 0, 0, 0))
        draw = ImageDraw.Draw(frame)
        c = BIG / 2
        radius = 6 + 40 * t
        fade = 1 - t ** 2
        draw.ellipse((c - radius, c - radius, c + radius, c + radius), outline=(40, 120, 220, int(210 * fade)), width=max(2, int(9 * (1 - t * 0.5))))
        draw.ellipse((c - radius - 2, c - radius - 2, c + radius + 2, c + radius + 2), outline=(235, 248, 255, int(255 * fade)), width=2)
        for tooth in range(28):
            angle = tooth * 2 * math.pi / 28 + t
            reach = radius + 3 + 2 * math.sin(tooth * 1.7 + t * 9)
            x, y = c + math.cos(angle) * reach, c + math.sin(angle) * reach
            draw.ellipse((x - 1.5, y - 1.5, x + 1.5, y + 1.5), fill=(235, 248, 255, int(240 * fade)))
        result.append(Image.alpha_composite(frame.filter(ImageFilter.GaussianBlur(1.5)), frame))
    return result


def write_signature():
    path = "Resources/Textures/_KS14/Cultivation/signature.rsi"
    os.makedirs(path, exist_ok=True)
    states = {
        "falling_rock": (falling_rock(), 0.07),
        "tide": (tide(), 0.06),
    }
    meta_states = []
    for name, (frames, delay) in states.items():
        big_sheet(frames).save(os.path.join(path, name + ".png"))
        meta_states.append({"name": name, "delays": [[delay] * len(frames)]})
    with open(os.path.join(path, "meta.json"), "w", encoding="utf-8") as file:
        json.dump({"version": 1, "license": "CC-BY-SA-3.0",
                   "copyright": "Generated for Klovnstation 14 by Tools/_KS14/cultivation_effects.py",
                   "size": {"x": BIG, "y": BIG}, "states": meta_states}, file, indent=2)
        file.write("\n")


PROTOTYPES = "Resources/Prototypes/_KS14/Cultivation/visual_effects.yml"

COLOURS = {
    "Qi": "#9fe3ff", "Gold": "#ffd55a", "Metal": "#d4dbea", "Water": "#4fb3ff", "Fire": "#ff6a1f",
    "Earth": "#c99560", "Wood": "#5fd35f", "Blood": "#d0251c", "Void": "#9b5cff", "Sense": "#d8f4ff",
}

# colour: (state, frames, delay) of the burst a cast in that colour makes
CAST_SHAPES = {
    "Qi": ("wave", 8, 0.06), "Gold": ("cast_rays", 8, 0.06), "Metal": ("cast_slash", 7, 0.06), "Water": ("cast_ripple", 9, 0.07),
    "Fire": ("cast_flare", 7, 0.06), "Earth": ("cast_rune", 8, 0.06), "Wood": ("cast_petals", 9, 0.06),
    "Blood": ("cast_spikes", 6, 0.05), "Void": ("cast_implode", 8, 0.06), "Sense": ("cast_ripple", 9, 0.07),
}

# name, state, scale, lifetime (None: stays until removed), light radius, light energy, draw depth, alpha
KINDS = [
    ("Wave", "wave", 1.5, 0.48, 3.0, 2.5, "Effects", "ff"),
    ("WaveBig", "wave", 4.0, 0.48, 6.0, 3.5, "Effects", "dd"),
    ("Spark", "spark", 1.0, 0.3, 2.0, 2.5, "Effects", "ff"),
    ("Motes", "motes", 1.0, 2.0, 1.5, 1.2, "Effects", "ee"),
    ("Aura", "aura", 1.0, None, 2.0, 1.4, "SmallMobs", "ee"),
    ("Dome", "dome", 1.0, None, 2.5, 1.5, "Effects", "aa"),
    ("Sigil", "sigil", 1.0, None, 2.0, 1.0, "FloorObjects", "cc"),
]


def write_prototypes():
    """One prototype per effect and element colour; each glows with its own coloured light."""
    out = ["# Generated by Tools/_KS14/cultivation_effects.py; edit the generator, not this file.",
           "# Glowing cultivation effects, one per element colour. Bursts (waves, sparks, motes) fade out on their own;",
           "# auras, domes and sigils follow a cultivator until CultivationVisualsSystem removes them.", ""]
    for name, state, scale, lifetime, radius, energy, depth, alpha in KINDS:
        for colour, hexcode in COLOURS.items():
            out += ["- type: entity", f"  id: KsFx{name}{colour}", "  categories: [ HideSpawnMenu ]", f"  name: {name.lower()}",
                    "  components:", "  - type: Sprite", "    sprite: _KS14/Cultivation/qi_effects.rsi", f"    drawdepth: {depth}", "    noRot: true",
                    f'    color: "{hexcode}{alpha}"', "    layers:", f"    - state: {state}", "      shader: unshaded"]
            if scale != 1.0:
                out.append(f"    scale: {scale}, {scale}")
            out += ["  - type: PointLight", f'    color: "{hexcode}"', f"    radius: {radius}", f"    energy: {energy}", "    netsync: false"]
            if lifetime is not None:
                out += ["  - type: LightFade", f"    duration: {lifetime}", "  - type: TimedDespawn", f"    lifetime: {lifetime}"]
            out.append("")
    # Each colour of qi is let loose in its own shape.
    for colour, hexcode in COLOURS.items():
        state, frames_count, delay = CAST_SHAPES[colour]
        lifetime = round(frames_count * delay, 2)
        for suffix, scale, radius, energy in (("", 1.6, 3.0, 2.5), ("Big", 4.0, 6.0, 3.5)):
            out += ["- type: entity", f"  id: KsFxCast{suffix}{colour}", "  categories: [ HideSpawnMenu ]", "  name: qi",
                    "  components:", "  - type: Sprite", "    sprite: _KS14/Cultivation/qi_effects.rsi", "    drawdepth: Effects", "    noRot: true",
                    f'    color: "{hexcode}"', f"    scale: {scale}, {scale}", "    layers:", f"    - state: {state}", "      shader: unshaded",
                    "  - type: PointLight", f'    color: "{hexcode}"', f"    radius: {radius}", f"    energy: {energy}", "    netsync: false",
                    "  - type: LightFade", f"    duration: {lifetime}", "  - type: TimedDespawn", f"    lifetime: {lifetime}", ""]

    out += ["- type: entity", "  id: KsFxPillar", "  categories: [ HideSpawnMenu ]", "  name: pillar of light", "  components:",
            "  - type: Sprite", "    sprite: _KS14/Cultivation/qi_effects.rsi", "    drawdepth: Effects", "    noRot: true", '    color: "#fff3b0"',
            "    scale: 1.5, 3", "    offset: 0, 1.5", "    layers:", "    - state: pillar", "      shader: unshaded", "  - type: PointLight",
            '    color: "#fff3b0"', "    radius: 7", "    energy: 5", "    netsync: false", "  - type: LightFade",
            "    duration: 0.8", "  - type: TimedDespawn", "    lifetime: 0.8"]
    with open(PROTOTYPES, "w", encoding="utf-8") as file:
        file.write("\n".join(out) + "\n")


def main():
    os.makedirs(OUT, exist_ok=True)
    states = {
        "wave": (wave(), 0.06),
        "spark": (spark(), 0.06),
        "motes": (motes(), 0.12),
        "pillar": (pillar(), 0.1),
        "aura": (aura(), 0.09),
        "dome": (dome(), 0.12),
        "sigil": (sigil(), 0.1),
        "cast_slash": (cast_slash(), 0.06),
        "cast_ripple": (cast_ripple(), 0.07),
        "cast_flare": (cast_flare(), 0.06),
        "cast_rune": (cast_rune(), 0.06),
        "cast_petals": (cast_petals(), 0.06),
        "cast_spikes": (cast_spikes(), 0.05),
        "cast_implode": (cast_implode(), 0.06),
        "cast_rays": (cast_rays(), 0.06),
    }
    meta_states = []
    for name, (frames, delay) in states.items():
        sheet(frames).save(os.path.join(OUT, name + ".png"))
        meta_states.append({"name": name, "delays": [[delay] * len(frames)]})

    meta = {
        "version": 1,
        "license": "CC-BY-SA-3.0",
        "copyright": "Generated for Klovnstation 14 by Tools/_KS14/cultivation_effects.py",
        "size": {"x": SIZE, "y": SIZE},
        "states": meta_states,
    }
    with open(os.path.join(OUT, "meta.json"), "w", encoding="utf-8") as file:
        json.dump(meta, file, indent=2)
        file.write("\n")

    write_signature()
    write_prototypes()


if __name__ == "__main__":
    main()
