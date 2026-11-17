#!/usr/bin/env python3
"""
Ports the SurfShack13 cultivation effect sprites to RSIs, and bakes their animations into frames.

SS13 moved, scaled and faded these sprites at runtime with animate() (code/modules/cultivation): the Buddha's Palm falls
from the sky onto its shadow and squashes into the floor, a void rift snaps open and pinches shut, rubble flies out as
particles. SS14 has no server-side tweening, so those motions are drawn out frame by frame here instead.

Usage: cultivation_ss13_sprites.py <path to the SurfShack13 checkout's icons/cultivation folder>
"""
import json
import math
import os
import random
import sys

from PIL import Image, ImageDraw, ImageFilter

TEXTURES = os.path.join(os.path.dirname(__file__), "..", "..", "Resources", "Textures", "_KS14", "Cultivation")
COPYRIGHT = "Ported from the SurfShack13 cultivation port; animations baked by Tools/_KS14/cultivation_ss13_sprites.py"


def read_dmi(path):
    """Every state of a DMI as {name: (frames, delays in seconds)}. All the cultivation DMIs are single-direction."""
    image = Image.open(path)
    width = height = None
    states = []
    for line in image.info["Description"].splitlines():
        line = line.strip()
        if line.startswith("width"):
            width = int(line.split("=")[1])
        elif line.startswith("height"):
            height = int(line.split("=")[1])
        elif line.startswith("state"):
            states.append({"name": line.split('"')[1], "frames": 1, "delay": None})
        elif line.startswith("frames"):
            states[-1]["frames"] = int(line.split("=")[1])
        elif line.startswith("delay"):
            states[-1]["delay"] = [float(value) / 10 for value in line.split("=")[1].split(",")]

    image = image.convert("RGBA")
    columns = image.width // width
    result = {}
    index = 0
    for state in states:
        frames = []
        for _ in range(state["frames"]):
            x, y = (index % columns) * width, (index // columns) * height
            frames.append(image.crop((x, y, x + width, y + height)))
            index += 1
        result[state["name"]] = (frames, state["delay"])
    return result


def write_rsi(name, size, states):
    """states: list of (state name, frames, delays or None)."""
    folder = os.path.join(TEXTURES, f"{name}.rsi")
    os.makedirs(folder, exist_ok=True)
    meta_states = []
    for state_name, frames, delays in states:
        columns = min(len(frames), 8)
        rows = math.ceil(len(frames) / columns)
        sheet = Image.new("RGBA", (columns * size[0], rows * size[1]))
        for index, frame in enumerate(frames):
            sheet.paste(frame, ((index % columns) * size[0], (index // columns) * size[1]))
        sheet.save(os.path.join(folder, f"{state_name}.png"))
        entry = {"name": state_name}
        if len(frames) > 1:
            entry["delays"] = [[round(delay, 3) for delay in delays]]
        meta_states.append(entry)
    meta = {"version": 1, "license": "CC-BY-SA-3.0", "copyright": COPYRIGHT,
            "size": {"x": size[0], "y": size[1]}, "states": meta_states}
    with open(os.path.join(folder, "meta.json"), "w") as meta_file:
        json.dump(meta, meta_file, indent=2)
        meta_file.write("\n")


def transformed(sprite, canvas, centre, scale_x=1.0, scale_y=1.0, alpha=1.0):
    """The sprite scaled about its own centre and pasted with its centre at `centre` on a blank canvas."""
    frame = Image.new("RGBA", canvas)
    width, height = max(1, round(sprite.width * scale_x)), max(1, round(sprite.height * scale_y))
    scaled = sprite.resize((width, height), Image.NEAREST)
    if alpha < 1.0:
        channel = scaled.getchannel("A").point(lambda value: int(value * alpha))
        scaled.putalpha(channel)
    frame.alpha_composite(scaled, (round(centre[0] - width / 2), round(centre[1] - height / 2)))
    return frame


def ease_in_quad(t):
    return t * t


def buddha_palm(palm):
    """
    Fades in high above, drops onto its target (quadratic ease in, as in SS13), squashes into the floor, springs back,
    then fades. Drawn at native size on a tall canvas: the entity scales it by 1.6 and offsets it so the landing point
    is the tile it was called down on.
    """
    canvas = (96, 256)
    landed = (48, 208)
    start = (48, 32)
    frames, delays = [], []

    for step in range(4):  # fade in, high up
        frames.append(transformed(palm, canvas, start, alpha=(step + 1) / 4))
        delays.append(0.1)
    for step in range(1, 17):  # the fall
        t = ease_in_quad(step / 16)
        frames.append(transformed(palm, canvas, (48, start[1] + (landed[1] - start[1]) * t)))
        delays.append(0.05)
    frames.append(transformed(palm, canvas, (landed[0], landed[1] + 6), scale_x=1.25, scale_y=0.75))  # squash
    delays.append(0.08)
    for scale_x, scale_y in [(0.92, 1.08), (1.05, 0.96), (0.98, 1.02), (1.0, 1.0)]:  # elastic spring back
        frames.append(transformed(palm, canvas, landed, scale_x=scale_x, scale_y=scale_y))
        delays.append(0.06)
    frames.append(transformed(palm, canvas, landed))
    delays.append(0.3)
    for step in range(6):  # fade out
        frames.append(transformed(palm, canvas, landed, alpha=1 - (step + 1) / 6))
        delays.append(0.1)
    return frames, delays


def palm_shadow(shadow):
    """Grows from a speck to its full spread and darkens while the palm falls, then lifts once it has landed."""
    canvas = (128, 128)
    frames, delays = [], []
    for step in range(12):
        t = ease_in_quad((step + 1) / 12)
        scale = 0.4 + 1.2 * t
        frames.append(transformed(shadow, canvas, (64, 64), scale, scale, alpha=0.47 * t + 0.05))
        delays.append(0.1)
    for step in range(4):
        frames.append(transformed(shadow, canvas, (64, 64), 1.6, 1.6, alpha=0.5 * (1 - (step + 1) / 4)))
        delays.append(0.1)
    return frames, delays


def void_rift(rift_frames):
    """Snaps open sideways in a fifth of a second, then pinches shut and fades, the rift churning all the while."""
    canvas = (64, 64)
    frames, delays = [], []
    for step in range(8):
        if step < 2:
            scale_x = 0.2 + 0.8 * (step + 1) / 2
            alpha = 1.0
        else:
            t = (step - 1) / 6
            scale_x = 1.0 - 0.9 * t
            alpha = 1.0 - t
        frames.append(transformed(rift_frames[step % len(rift_frames)], canvas, (32, 32), scale_x, 1.0, alpha))
        delays.append(0.1)
    return frames, delays


def rubble(chips):
    """A burst of flying rock chips under gravity, like the SS13 rubble particles."""
    rng = random.Random(1337)
    canvas = (64, 64)
    particles = []
    for index in range(40):
        angle = rng.uniform(0, math.tau)
        speed = rng.uniform(3, 6)
        radius = abs(rng.gauss(0, 4))
        particles.append({
            "chip": chips[index % len(chips)],
            "x": 32 + math.cos(angle) * radius, "y": 32 + math.sin(angle) * radius,
            "vx": math.cos(angle) * speed, "vy": math.sin(angle) * speed - 2,
            "born": index // 20,
            "spin": rng.choice([0, 90, 180, 270]),
            "scale": rng.uniform(0.8, 1.4),
        })
    frames, delays = [], []
    for frame_index in range(12):
        frame = Image.new("RGBA", canvas)
        for particle in particles:
            age = frame_index - particle["born"]
            if age < 0 or age > 9:
                continue
            x = particle["x"] + particle["vx"] * age
            y = particle["y"] + particle["vy"] * age + 0.4 * age * age
            alpha = 1.0 if age < 6 else 1 - (age - 5) / 4
            chip = particle["chip"].rotate(particle["spin"])
            frame.alpha_composite(transformed(chip, canvas, (x, y), particle["scale"], particle["scale"], alpha))
        frames.append(frame)
        delays.append(0.07)
    return frames, delays


def vines(grow, sway, hold_seconds=3.0):
    """Shoots up, then sways until it lets go."""
    frames = list(grow)
    delays = [0.1] * len(grow)
    while sum(delays) < hold_seconds:
        frames += sway
        delays += [0.3] * len(sway)
    return frames, delays


# ---------------------------------------------------------------- the arts: one animation each, on a 96 pixel canvas

ARTS = (96, 96)


def tinted(sprite, colour):
    """The sprite's shading kept, its colour replaced."""
    grey = sprite.convert("LA")
    result = Image.new("RGBA", sprite.size)
    pixels = result.load()
    source = grey.load()
    for y in range(sprite.height):
        for x in range(sprite.width):
            value, alpha = source[x, y]
            light = 0.45 + 0.75 * value / 255
            pixels[x, y] = (min(255, int(colour[0] * light)), min(255, int(colour[1] * light)), min(255, int(colour[2] * light)), alpha)
    return result


def glow(frame, radius=3, strength=1.0):
    """A soft halo under the frame, so it reads as light."""
    halo = frame.filter(ImageFilter.GaussianBlur(radius))
    if strength != 1.0:
        halo.putalpha(halo.getchannel("A").point(lambda value: min(255, int(value * strength))))
    return Image.alpha_composite(halo, frame)


def with_alpha(frame, alpha):
    if alpha >= 1.0:
        return frame
    frame = frame.copy()
    frame.putalpha(frame.getchannel("A").point(lambda value: int(value * alpha)))
    return frame


def palm_print(palm, colour):
    """A hand of qi slapped onto the target: it punches in big and bright, holds, and fades as its ring spreads."""
    hand = tinted(palm, colour).resize((40, 40), Image.NEAREST)
    frames, delays = [], []
    for step, (scale, alpha) in enumerate([(1.7, 0.5), (1.25, 0.9), (1.0, 1.0), (0.94, 1.0), (1.0, 1.0)]):
        frame = transformed(hand, ARTS, (48, 48), scale, scale, alpha)
        frames.append(glow(frame, 3, 1.4))
        delays.append(0.05)
    for step in range(6):
        t = (step + 1) / 6
        frame = Image.new("RGBA", ARTS)
        draw = ImageDraw.Draw(frame)
        radius = 18 + 26 * t
        draw.ellipse((48 - radius, 48 - radius, 48 + radius, 48 + radius), outline=colour + (int(200 * (1 - t)),), width=2)
        frame.alpha_composite(transformed(hand, ARTS, (48, 48), 1.0 + 0.15 * t, 1.0 + 0.15 * t, 1 - t))
        frames.append(glow(frame, 2))
        delays.append(0.07)
    return frames, delays


def sword_rain(sword):
    """Five blades dropping point first out of the sky, staggered, each flashing where it bites."""
    rng = random.Random(7)
    blade = sword.resize((32, 32), Image.NEAREST).rotate(180)
    drops = [(rng.uniform(18, 78), rng.uniform(40, 80), index * 1.6) for index in range(5)]
    frames, delays = [], []
    for frame_index in range(14):
        frame = Image.new("RGBA", ARTS)
        draw = ImageDraw.Draw(frame)
        for x, land_y, start in drops:
            t = (frame_index - start) / 4
            if t < 0:
                continue
            if t <= 1:
                y = -12 + (land_y + 12) * t * t
                trail = Image.new("RGBA", ARTS)
                ImageDraw.Draw(trail).line((x, y - 22, x, y), fill=(200, 230, 255, 120), width=2)
                frame.alpha_composite(trail)
                frame.alpha_composite(transformed(blade, ARTS, (x, y)))
            else:
                after = t - 1
                if after > 1.2:
                    continue
                frame.alpha_composite(transformed(blade, ARTS, (x, land_y - 4), alpha=max(0.0, 1 - after)))
                radius = 3 + 9 * after
                draw.ellipse((x - radius, land_y - radius / 2, x + radius, land_y + radius / 2),
                             outline=(220, 240, 255, int(230 * max(0.0, 1 - after))), width=2)
        frames.append(glow(frame, 2))
        delays.append(0.06)
    return frames, delays


def sword_orbit(sword):
    """Three blades wheeling round their master, points outward."""
    blade = sword.resize((22, 22), Image.NEAREST)
    frames, delays = [], []
    for frame_index in range(12):
        frame = Image.new("RGBA", ARTS)
        for blade_index in range(3):
            angle = 2 * math.pi * (frame_index / 12 + blade_index / 3)
            x, y = 48 + math.cos(angle) * 30, 52 + math.sin(angle) * 14
            facing = blade.rotate(-math.degrees(angle) - 90, expand=True)
            frame.alpha_composite(transformed(facing, ARTS, (x, y)))
        frames.append(glow(frame, 2))
        delays.append(0.08)
    return frames, delays


def talisman_flare(talisman):
    """The paper lifts, flashes orange, and burns away into embers, as in SS13."""
    paper = talisman.resize((40, 40), Image.NEAREST)
    rng = random.Random(3)
    sparks = [(rng.uniform(-10, 10), rng.uniform(-6, 6), rng.uniform(-0.6, 0.6), rng.uniform(1.2, 2.4)) for _ in range(16)]
    frames, delays = [], []
    for step in range(10):
        frame = Image.new("RGBA", ARTS)
        if step < 3:
            t = (step + 1) / 3
            frame.alpha_composite(transformed(paper, ARTS, (48, 56 - 10 * t), 1 + 0.3 * t, 1 + 0.3 * t))
            frame = glow(frame, 3, 0.8 + t)
        else:
            t = (step - 2) / 7
            burning = tinted(paper, (255, 120, 40))
            frame.alpha_composite(transformed(burning, ARTS, (48, 46), 1.3 - 0.7 * t, 1.3 - 0.7 * t, 1 - t))
            draw = ImageDraw.Draw(frame)
            for dx, dy, vx, vy in sparks:
                age = step - 2
                x, y = 48 + dx + vx * age * 3, 46 + dy - vy * age * 3
                draw.rectangle((x, y, x + 1, y + 1), fill=(255, 180 - age * 10, 60, int(255 * (1 - t))))
            frame = glow(frame, 2)
        frames.append(frame)
        delays.append(0.07)
    return frames, delays


def particle_burst(mote, mode, count=26, seed=11):
    """Bursts of SS13's own particles: embers rising, petals drifting down, motes rising or swirling inward."""
    rng = random.Random(seed)
    particles = []
    for _ in range(count):
        angle = rng.uniform(0, math.tau)
        radius = rng.uniform(4, 30)
        particles.append({"angle": angle, "radius": radius, "born": rng.randint(0, 5),
                          "drift": rng.uniform(-0.6, 0.6), "speed": rng.uniform(1.5, 3.2), "spin": rng.uniform(-30, 30),
                          "scale": rng.uniform(0.8, 1.5)})
    frames, delays = [], []
    for frame_index in range(14):
        frame = Image.new("RGBA", ARTS)
        for particle in particles:
            age = frame_index - particle["born"]
            if age < 0 or age > 8:
                continue
            fade = 1.0 if age < 5 else 1 - (age - 4) / 5
            if mode == "rise":
                x = 48 + math.cos(particle["angle"]) * particle["radius"] * 0.8 + particle["drift"] * age * 2
                y = 64 + math.sin(particle["angle"]) * particle["radius"] * 0.4 - particle["speed"] * age * 3
            elif mode == "fall":
                x = 48 + math.cos(particle["angle"]) * particle["radius"] * 1.3 + particle["drift"] * age * 4
                y = 20 + math.sin(particle["angle"]) * 8 + particle["speed"] * age * 2.5
            else:  # swirl inward
                radius = particle["radius"] * 1.3 * (1 - age / 9)
                angle = particle["angle"] + age * 0.35
                x, y = 48 + math.cos(angle) * radius, 48 + math.sin(angle) * radius
            sprite = mote.rotate(particle["spin"] * age)
            frame.alpha_composite(transformed(sprite, ARTS, (x, y), particle["scale"], particle["scale"], fade))
        frames.append(glow(frame, 2, 1.3))
        delays.append(0.07)
    return frames, delays


def fist_burst():
    """A blow landing: a white flash, speed lines bursting outward, a jagged ring of force. Drawn white, tinted per art."""
    rng = random.Random(5)
    lines = [(rng.uniform(0, math.tau), rng.uniform(0.7, 1.2)) for _ in range(14)]
    frames, delays = [], []
    for step in range(8):
        t = (step + 1) / 8
        frame = Image.new("RGBA", ARTS)
        draw = ImageDraw.Draw(frame)
        if step < 3:
            radius = 8 + 6 * step
            draw.ellipse((48 - radius, 48 - radius, 48 + radius, 48 + radius), fill=(255, 255, 255, 220 - 60 * step))
        for angle, length in lines:
            inner, outer = 10 + 30 * t, (16 + 34 * t) * length
            draw.line((48 + math.cos(angle) * inner, 48 + math.sin(angle) * inner,
                       48 + math.cos(angle) * outer, 48 + math.sin(angle) * outer), fill=(255, 255, 255, int(255 * (1 - t))), width=2)
        points = []
        for index in range(24):
            angle = index / 24 * math.tau
            radius = (12 + 30 * t) * (1 + 0.12 * (index % 2))
            points.append((48 + math.cos(angle) * radius, 48 + math.sin(angle) * radius))
        draw.polygon(points, outline=(255, 255, 255, int(220 * (1 - t))))
        frames.append(glow(frame, 2, 1.3))
        delays.append(0.05)
    return frames, delays


def barrage():
    """A hundred fists: little starbursts popping all over the target, faster than the eye."""
    rng = random.Random(9)
    hits = [(rng.uniform(26, 70), rng.uniform(26, 70), index // 2) for index in range(28)]
    frames, delays = [], []
    for frame_index in range(16):
        frame = Image.new("RGBA", ARTS)
        draw = ImageDraw.Draw(frame)
        for x, y, born in hits:
            age = frame_index - born
            if age < 0 or age > 2:
                continue
            size = 4 + 4 * age
            alpha = int(255 * (1 - age / 3))
            for angle in range(0, 360, 45):
                length = size if angle % 90 == 0 else size * 0.6
                draw.line((x, y, x + math.cos(math.radians(angle)) * length, y + math.sin(math.radians(angle)) * length),
                          fill=(255, 255, 255, alpha), width=2 if angle % 90 == 0 else 1)
        frames.append(glow(frame, 2, 1.2))
        delays.append(0.04)
    return frames, delays


def fire_ring():
    """A ring of flame tongues bursting outward from the furnace of the dantian."""
    rng = random.Random(13)
    tongues = [(index / 30 * math.tau + rng.uniform(-0.05, 0.05), rng.uniform(0.7, 1.3)) for index in range(30)]
    frames, delays = [], []
    for step in range(10):
        t = (step + 1) / 10
        frame = Image.new("RGBA", ARTS)
        draw = ImageDraw.Draw(frame)
        radius = 8 + 36 * t
        for angle, height in tongues:
            flicker = height * (1 + 0.3 * math.sin(step * 2 + angle * 5))
            base_x, base_y = 48 + math.cos(angle) * radius, 48 + math.sin(angle) * radius
            tip_x, tip_y = 48 + math.cos(angle) * (radius + 10 * flicker), 48 + math.sin(angle) * (radius + 10 * flicker)
            side = 0.12
            left = (48 + math.cos(angle - side) * radius, 48 + math.sin(angle - side) * radius)
            right = (48 + math.cos(angle + side) * radius, 48 + math.sin(angle + side) * radius)
            alpha = int(255 * (1 - t * t))
            draw.polygon([left, (tip_x, tip_y), right], fill=(255, 110 + int(80 * (1 - t)), 30, alpha))
            draw.line((base_x, base_y, (base_x + tip_x) / 2, (base_y + tip_y) / 2), fill=(255, 240, 160, alpha), width=2)
        frames.append(glow(frame, 3, 1.4))
        delays.append(0.06)
    return frames, delays


def blood_vortex():
    """Demonic qi spiralling in to be devoured."""
    frames, delays = [], []
    for step in range(12):
        frame = Image.new("RGBA", ARTS)
        draw = ImageDraw.Draw(frame)
        for arm in range(4):
            points = []
            for index in range(30):
                u = index / 29
                radius = 44 * (1 - u) * (1 - step / 16)
                angle = arm * math.pi / 2 + u * 4.5 + step * 0.45
                points.append((48 + math.cos(angle) * radius, 48 + math.sin(angle) * radius))
            for index in range(len(points) - 1):
                alpha = int(230 * (index / len(points)))
                draw.line(points[index] + points[index + 1], fill=(170, 20, 40, alpha), width=3)
        core = 4 + step % 3
        draw.ellipse((48 - core, 48 - core, 48 + core, 48 + core), fill=(40, 0, 10, 230))
        frames.append(glow(frame, 3, 1.3))
        delays.append(0.07)
    return frames, delays


def roar_rings():
    """A roar you can see: arcs of shaken air racing outward, one after another. Drawn white, tinted per art."""
    frames, delays = [], []
    for step in range(12):
        frame = Image.new("RGBA", ARTS)
        draw = ImageDraw.Draw(frame)
        for ring in range(3):
            t = (step - ring * 3) / 8
            if not 0 < t <= 1:
                continue
            radius = 6 + 40 * t
            alpha = int(240 * (1 - t))
            draw.ellipse((48 - radius, 48 - radius, 48 + radius, 48 + radius), outline=(255, 255, 255, alpha), width=4 - ring)
            draw.ellipse((48 - radius * 0.85, 48 - radius * 0.85, 48 + radius * 0.85, 48 + radius * 0.85), outline=(255, 255, 255, alpha // 3), width=1)
        frames.append(glow(frame, 2))
        delays.append(0.06)
    return frames, delays


def fissures():
    """The ground splitting open: cracks shoot outward from the centre, glowing hot inside, then go dark."""
    rng = random.Random(17)
    cracks = []
    for index in range(9):
        angle = index / 9 * math.tau + rng.uniform(-0.2, 0.2)
        points = [(48.0, 48.0)]
        for _ in range(12):
            angle += rng.uniform(-0.35, 0.35)
            last = points[-1]
            points.append((last[0] + math.cos(angle) * 3.5, last[1] + math.sin(angle) * 3.5))
        cracks.append(points)
    frames, delays = [], []
    for step in range(12):
        frame = Image.new("RGBA", ARTS)
        draw = ImageDraw.Draw(frame)
        grown = min(12, 3 + step * 2)
        heat = 1.0 if step < 6 else 1 - (step - 5) / 6
        for points in cracks:
            for index in range(grown):
                width = 4 if index < 4 else (3 if index < 8 else 2)
                draw.line(points[index] + points[index + 1], fill=(30, 22, 18, int(255 * max(heat, 0.3))), width=width)
                draw.line(points[index] + points[index + 1], fill=(255, 140, 40, int(220 * heat)), width=1)
        frames.append(glow(frame, 2, heat))
        delays.append(0.06)
    return frames, delays


def falling_star():
    """Plunging out of the sky like a meteor: a burning streak, then the flash of the landing."""
    frames, delays = [], []
    for step in range(10):
        frame = Image.new("RGBA", ARTS)
        draw = ImageDraw.Draw(frame)
        if step < 5:
            t = (step + 1) / 5
            x, y = 48 - 40 * (1 - t), 48 - 60 * (1 - t) ** 1.3
            for trail in range(10):
                u = trail / 10
                draw.ellipse((x - 40 * u * 0.5 - 4 * (1 - u), y - 60 * u * 0.5 - 4 * (1 - u), x - 40 * u * 0.5 + 4 * (1 - u), y - 60 * u * 0.5 + 4 * (1 - u)),
                             fill=(255, 180 + int(60 * (1 - u)), 80, int(240 * (1 - u))))
            draw.ellipse((x - 5, y - 5, x + 5, y + 5), fill=(255, 255, 230, 255))
        else:
            t = (step - 4) / 5
            radius = 6 + 34 * t
            draw.ellipse((48 - radius, 48 - radius * 0.6, 48 + radius, 48 + radius * 0.6), fill=(255, 220, 140, int(110 * (1 - t))))
            draw.ellipse((48 - radius * 1.2, 48 - radius * 0.72, 48 + radius * 1.2, 48 + radius * 0.72), outline=(255, 160, 60, int(255 * (1 - t))), width=3)
        frames.append(glow(frame, 3, 1.4))
        delays.append(0.05)
    return frames, delays


def sheen():
    """Skin turning to iron: a bright band sweeps down over the body's outline. Drawn white, tinted per art."""
    frames, delays = [], []
    for step in range(10):
        frame = Image.new("RGBA", ARTS)
        draw = ImageDraw.Draw(frame)
        draw.ellipse((30, 20, 66, 80), outline=(255, 255, 255, 120), width=2)
        band = Image.new("RGBA", ARTS)
        band_draw = ImageDraw.Draw(band)
        y = 10 + step * 9
        band_draw.polygon([(20, y - 6), (76, y - 14), (76, y - 4), (20, y + 4)], fill=(255, 255, 255, 230))
        mask = Image.new("L", ARTS)
        ImageDraw.Draw(mask).ellipse((30, 20, 66, 80), fill=255)
        frame.paste(band, (0, 0), Image.composite(band, Image.new("RGBA", ARTS), mask).getchannel("A"))
        frames.append(glow(with_alpha(frame, 1 - max(0, step - 6) / 4), 2, 1.2))
        delays.append(0.05)
    return frames, delays


def mend_glow():
    """Healing: a soft glow and little crosses of light rising through the body. Drawn white, tinted per art."""
    rng = random.Random(21)
    sparkles = [(rng.uniform(30, 66), rng.uniform(56, 82), rng.randint(0, 6), rng.uniform(2, 3.2)) for _ in range(12)]
    frames, delays = [], []
    for step in range(14):
        frame = Image.new("RGBA", ARTS)
        draw = ImageDraw.Draw(frame)
        pulse = math.sin(step / 13 * math.pi)
        draw.ellipse((26, 22, 70, 84), fill=(255, 255, 255, int(70 * pulse)))
        for x, y, born, speed in sparkles:
            age = step - born
            if age < 0 or age > 7:
                continue
            cy = y - age * speed * 2
            alpha = int(255 * (1 - age / 8))
            draw.line((x - 3, cy, x + 3, cy), fill=(255, 255, 255, alpha), width=2)
            draw.line((x, cy - 3, x, cy + 3), fill=(255, 255, 255, alpha), width=2)
        frames.append(glow(frame, 3, 1.2))
        delays.append(0.07)
    return frames, delays


def mist():
    """Puffs billowing out and thinning away. Drawn white, tinted per art (blood mist, steam)."""
    rng = random.Random(25)
    puffs = [(rng.uniform(0, math.tau), rng.uniform(4, 22), rng.uniform(8, 14), rng.randint(0, 3)) for _ in range(14)]
    frames, delays = [], []
    for step in range(12):
        frame = Image.new("RGBA", ARTS)
        draw = ImageDraw.Draw(frame)
        for angle, distance, size, born in puffs:
            age = step - born
            if age < 0:
                continue
            t = age / 10
            if t > 1:
                continue
            radius = size * (0.6 + t)
            x, y = 48 + math.cos(angle) * distance * (1 + t), 52 + math.sin(angle) * distance * 0.7 * (1 + t) - 6 * t
            draw.ellipse((x - radius, y - radius, x + radius, y + radius), fill=(255, 255, 255, int(150 * (1 - t))))
        frames.append(frame.filter(ImageFilter.GaussianBlur(2)))
        delays.append(0.08)
    return frames, delays


# Every art effect: (prototype id, state, tint or None, scale, light colour or None, draw depth).
ART_PROTOTYPES = [
    ("KsFxArtPalmQi", "palm_qi", None, 1.0, "#78c8ff", "Effects"),
    ("KsFxArtPalmFire", "palm_fire", None, 1.0, "#ff8228", "Effects"),
    ("KsFxArtPalmBlood", "palm_blood", None, 1.0, "#c82846", "Effects"),
    ("KsFxArtPalmGold", "palm_gold", None, 1.4, "#ffd250", "Effects"),
    ("KsFxArtSwordRain", "sword_rain", None, 1.0, "#d8f0ff", "Overdoors"),
    ("KsFxArtSwordOrbit", "sword_orbit", None, 1.0, "#d8f0ff", "Overdoors"),
    ("KsFxArtTalismanFlare", "talisman_flare", None, 1.0, "#ffb347", "Overdoors"),
    ("KsFxArtEmbers", "embers", None, 1.0, "#ff7a1f", "Overdoors"),
    ("KsFxArtPetals", "petals", None, 1.0, None, "Overdoors"),
    ("KsFxArtPetalsBig", "petals", None, 2.0, None, "Overdoors"),
    ("KsFxArtGoldMotes", "gold_motes", None, 1.0, "#ffd55a", "Overdoors"),
    ("KsFxArtVoidMotes", "void_motes", None, 1.0, "#8a5cff", "Overdoors"),
    ("KsFxArtFistGold", "fist_burst", "#ffd96a", 1.0, "#ffd55a", "Effects"),
    ("KsFxArtFistEarth", "fist_burst", "#e0b070", 1.3, "#c8a060", "Effects"),
    ("KsFxArtFistQi", "fist_burst", "#a8dcff", 0.7, "#78c8ff", "Effects"),
    ("KsFxArtBarrage", "barrage", "#ffe8a0", 1.0, "#ffd55a", "Effects"),
    ("KsFxArtFireRing", "fire_ring", None, 1.0, "#ff7a1f", "Effects"),
    ("KsFxArtFireRingBig", "fire_ring", None, 2.2, "#ff7a1f", "Effects"),
    ("KsFxArtBloodVortex", "blood_vortex", None, 1.0, "#aa1428", "Effects"),
    ("KsFxArtRoarBlood", "roar_rings", "#ff9a9a", 3.0, "#c82846", "Effects"),
    ("KsFxArtRoarGold", "roar_rings", "#ffe27a", 2.0, "#ffd55a", "Effects"),
    ("KsFxArtRoarSense", "roar_rings", "#d0c0ff", 2.5, "#b49cff", "Effects"),
    ("KsFxArtFissures", "fissures", None, 1.5, "#ff8c28", "FloorObjects"),
    ("KsFxArtFissuresBig", "fissures", None, 3.0, "#ff8c28", "FloorObjects"),
    ("KsFxArtFallingStar", "falling_star", None, 1.5, "#ffb450", "Overdoors"),
    ("KsFxArtSheenIron", "sheen", "#c8d2dc", 1.0, "#c8d2dc", "Overdoors"),
    ("KsFxArtSheenGold", "sheen", "#ffd96a", 1.0, "#ffd55a", "Overdoors"),
    ("KsFxArtMendWood", "mend_glow", "#8af08a", 1.0, "#6fd06f", "Overdoors"),
    ("KsFxArtMendBone", "mend_glow", "#fff6e0", 1.0, "#fff0c8", "Overdoors"),
    ("KsFxArtMendWater", "mend_glow", "#a8dcff", 1.0, "#5ab8ff", "Overdoors"),
    ("KsFxArtMistBlood", "mist", "#c0283c", 1.2, "#aa1428", "Overdoors"),
    ("KsFxArtMistSteam", "mist", "#f0f0f0", 1.2, None, "Overdoors"),
    ("KsFxArtMistDust", "mist", "#a09078", 1.0, None, "Effects"),
    ("KsFxArtMistShadow", "mist", "#3c2850", 1.2, None, "Overdoors"),
]


def write_art_prototypes(delays_by_state):
    """The prototypes for the arts, each despawning as its one run of frames ends."""
    lines = ["# Generated by Tools/_KS14/cultivation_ss13_sprites.py: each art's own animation, despawning as its run ends.", ""]
    for proto_id, state, tint, scale, light, depth in ART_PROTOTYPES:
        lines += ["- type: entity", f"  id: {proto_id}", "  categories: [ HideSpawnMenu ]", "  name: technique", "  components:",
                  "  - type: Sprite", "    sprite: _KS14/Cultivation/wuxia_arts.rsi", f"    drawdepth: {depth}", "    noRot: true", "    layers:",
                  f"    - state: {state}", "      shader: unshaded"]
        if tint:
            lines.append(f'      color: "{tint}"')
        if scale != 1.0:
            lines.append(f"      scale: {scale}, {scale}")
        if light:
            lines += ["  - type: PointLight", f'    color: "{light}"', f"    radius: {round(2 + 1.5 * scale, 1)}", "    energy: 2", "    netsync: false"]
        lifetime = round(sum(delays_by_state[state]) - 0.01, 2)
        lines += ["  - type: TimedDespawn", f"    lifetime: {lifetime}", ""]
    path = os.path.join(os.path.dirname(__file__), "..", "..", "Resources", "Prototypes", "_KS14", "Cultivation", "wuxia_arts.yml")
    with open(path, "w") as prototype_file:
        prototype_file.write("\n".join(lines))


# (rsi, our state, SS13 item state): the SS13 originals that replace generated icons.
SS13_ITEMS = [
    ("manuals", "manual_metal", "manual_metal"),
    ("manuals", "manual_water", "manual_water"),
    ("manuals", "manual_fire", "manual_fire"),
    ("manuals", "manual_earth", "manual_earth"),
    ("manuals", "manual_wood", "manual_wood"),
    ("manuals", "manual_demonic", "manual_demonic"),
    ("manuals", "body_primer", "manual_body_primer"),
    ("manuals", "body_molding_art", "manual_body"),
    ("items", "needles", "needles"),
    ("items", "flying_dagger", "flying_dagger"),
    ("items", "smoke_pellet", "smoke_pellet"),
    ("items", "talisman", "talisman"),
    ("items", "sect_plaque", "sect_plaque"),
]


def main():
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    icons = sys.argv[1]
    effects32 = read_dmi(os.path.join(icons, "cultivation_effects.dmi"))
    effects64 = read_dmi(os.path.join(icons, "cultivation_effects_64.dmi"))
    effects96 = read_dmi(os.path.join(icons, "cultivation_effects_96.dmi"))
    particles = read_dmi(os.path.join(icons, "cultivation_particles.dmi"))
    terrain = read_dmi(os.path.join(icons, "cultivation_terrain.dmi"))
    idol = read_dmi(os.path.join(icons, "dharma_idol.dmi"))

    def plain(source, name, rename=None):
        frames, delays = source[name]
        return rename or name, frames, delays

    write_rsi("buddha_palm", (96, 256), [("palm", *buddha_palm(effects64["buddha_palm"][0][0]))])
    write_rsi("wuxia_128", (128, 128), [("palm_shadow", *palm_shadow(effects64["buddha_palm_shadow"][0][0]))])

    chips = [particles[f"rubble_{index}"][0][0] for index in (1, 2, 3)]
    write_rsi("wuxia_64", (64, 64), [
        plain(effects64, "crater"),
        plain(effects64, "golden_bell"),
        plain(effects64, "water_bubble"),
        plain(effects64, "heart_demon_aura"),
        plain(effects64, "beast_aura"),
        plain(effects64, "dragon_aura"),
        plain(effects64, "decree_scroll"),
        plain(idol, "dharma_idol"),
        ("void_rift", *void_rift(effects64["void_rift"][0])),
        ("rubble", *rubble(chips)),
    ])
    write_rsi("wuxia_32", (32, 32), [
        plain(effects32, "orbit_sword"),
        plain(effects32, "sword_qi"),
        plain(effects32, "sword_qi_impact"),
        plain(effects32, "thorn_sword"),
        plain(effects32, "thorn_splinters"),
        plain(terrain, "mud"),
        plain(terrain, "molten_steps"),
        ("vines", *vines(terrain["vines_grow"][0], terrain["vines_sway"][0])),
    ])
    items = read_dmi(os.path.join(icons, "cultivation_items.dmi"))
    riding = read_dmi(os.path.join(icons, "cultivation_riding.dmi"))
    palm = effects64["buddha_palm"][0][0]
    sword = effects32["orbit_sword"][0][0]
    arts = [
        ("palm_qi", *palm_print(palm, (120, 200, 255))),
        ("palm_fire", *palm_print(palm, (255, 130, 40))),
        ("palm_blood", *palm_print(palm, (200, 40, 70))),
        ("palm_gold", *palm_print(palm, (255, 210, 80))),
        ("sword_rain", *sword_rain(sword)),
        ("sword_orbit", *sword_orbit(sword)),
        ("talisman_flare", *talisman_flare(items["talisman"][0][0])),
        ("embers", *particle_burst(particles["ember"][0][0], "rise")),
        ("petals", *particle_burst(particles["petal"][0][0], "fall")),
        ("gold_motes", *particle_burst(particles["gold_mote"][0][0], "rise", seed=12)),
        ("void_motes", *particle_burst(particles["void_mote"][0][0], "swirl", seed=13)),
        ("fist_burst", *fist_burst()),
        ("barrage", *barrage()),
        ("fire_ring", *fire_ring()),
        ("blood_vortex", *blood_vortex()),
        ("roar_rings", *roar_rings()),
        ("fissures", *fissures()),
        ("falling_star", *falling_star()),
        ("sheen", *sheen()),
        ("mend_glow", *mend_glow()),
        ("mist", *mist()),
    ]
    write_rsi("wuxia_arts", ARTS, arts)
    write_art_prototypes({name: delays for name, _, delays in arts})
    # Where SS13 drew an item better than cultivation_icons.py does, its sprite replaces ours (run this after that script).
    for rsi, ours, theirs in SS13_ITEMS:
        frames, _ = items[theirs]
        frames[0].save(os.path.join(TEXTURES, f"{rsi}.rsi", f"{ours}.png"))

    write_rsi("wuxia_riding", (64, 64), [plain(riding, "sword_ride"), plain(riding, "qi_cloud")])
    write_rsi("wuxia_96", (96, 96), [
        plain(effects96, "storm_cloud"),
        plain(effects96, "sigil_barrier"),
        plain(effects96, "sigil_gathering"),
        plain(effects96, "sigil_alarm"),
    ])


if __name__ == "__main__":
    main()
