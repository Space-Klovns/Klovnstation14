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

from PIL import Image

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
    write_rsi("wuxia_96", (96, 96), [
        plain(effects96, "storm_cloud"),
        plain(effects96, "sigil_barrier"),
        plain(effects96, "sigil_gathering"),
        plain(effects96, "sigil_alarm"),
    ])


if __name__ == "__main__":
    main()
