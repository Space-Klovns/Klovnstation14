#!/usr/bin/env python3
# KS14: added in this fork
"""
Paints the legendary armor of the jianghu, piece by piece, onto the human body's own part masks so that every
direction lines up with the mob underneath:

  - the Great Sage's regalia from Journey to the West: Golden Chain-Mail, Phoenix-Wing Purple-Gold Crown and
    Lotus-Silk Cloud-Stepping Boots, in gilded scale and black lacquer;
  - Soft Hedgehog Armor (Legend of the Condor Heroes): black silk bristling with barbs;
  - the Golden Silk Vest (The Deer and the Cauldron): a fine woven treasure vest with jade clasps.

Run from the repository root (needs Pillow):

    python3 Tools/_KS14/cultivation_armor.py
"""

import json
import os

from PIL import Image

TEXTURES = "Resources/Textures/_KS14/Cultivation"
PARTS = "Resources/Textures/Mobs/Species/Human/parts.rsi"
COPYRIGHT = "Drawn for Klovnstation 14 by Tools/_KS14/cultivation_armor.py, fitted to the human body parts"
LICENSE = "CC-BY-SA-3.0"

# Frame offsets inside a four-direction sheet, in RSI order.
FRAMES = {"S": (0, 0), "N": (32, 0), "E": (0, 32), "W": (32, 32)}

LACQUER_DARK = (20, 18, 26)
LACQUER = (36, 32, 46)
LACQUER_HIGH = (60, 54, 74)
GOLD_DARK = (122, 80, 22)
GOLD = (198, 150, 48)
GOLD_HIGH = (250, 214, 112)
GOLD_SHINE = (255, 246, 206)
PURPLE = (94, 50, 120)
PURPLE_HIGH = (140, 84, 172)
RED = (176, 32, 42)
JADE = (70, 184, 140)
FEATHER_LIGHT = (232, 222, 200)
FEATHER_DARK = (122, 72, 40)
FEATHER_TIP = (40, 26, 20)
LOTUS = (232, 182, 192)
LOTUS_DARK = (178, 112, 134)
CLOUD = (236, 248, 255)
SILK_BLACK = (30, 28, 34)
SILK_SHEEN = (58, 56, 66)
BARB = (160, 164, 176)
BARB_DARK = (90, 92, 104)


def load_masks():
    """For each body part and direction, the set of pixels it covers."""
    masks = {}
    for part in ["head_m", "torso_m", "l_arm", "r_arm", "l_hand", "r_hand", "l_leg", "r_leg", "l_foot", "r_foot"]:
        sheet = Image.open(os.path.join(PARTS, part + ".png")).convert("RGBA")
        masks[part] = {}
        for direction, (ox, oy) in FRAMES.items():
            frame = sheet.crop((ox, oy, ox + 32, oy + 32))
            masks[part][direction] = {(x, y) for y in range(32) for x in range(32) if frame.getpixel((x, y))[3] > 0}
    return masks


def bounds(pixels):
    xs = [x for x, _ in pixels]
    ys = [y for _, y in pixels]
    return min(xs), min(ys), max(xs), max(ys)


def outline(image, factor=0.45):
    """Darken every painted pixel on the silhouette's edge, as SS14 clothing does."""
    pixels = image.load()
    edge = []
    for y in range(image.height):
        for x in range(image.width):
            if pixels[x, y][3] == 0:
                continue
            for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
                if not (0 <= nx < image.width and 0 <= ny < image.height) or pixels[nx, ny][3] == 0:
                    edge.append((x, y))
                    break
    for x, y in edge:
        r, g, b, a = pixels[x, y]
        pixels[x, y] = (int(r * factor), int(g * factor), int(b * factor), a)


def put(image, x, y, colour):
    if 0 <= x < image.width and 0 <= y < image.height:
        image.putpixel((x, y), colour + (255,))


# ---------------------------------------------------------------- Golden Chain-Mail

def chain_mail(masks, direction):
    image = Image.new("RGBA", (32, 32))
    torso = masks["torso_m"][direction]
    arms = masks["l_arm"][direction] | masks["r_arm"][direction]
    legs = masks["l_leg"][direction] | masks["r_leg"][direction]
    left, top, right, bottom = bounds(torso)
    centre = (left + right) // 2

    # Black lacquered lamellae, with a front panel of gold fish-scale mail between gilded edges.
    for x, y in torso:
        if y == top:
            colour = GOLD
        elif y == top + 1:
            colour = LACQUER_DARK
        elif abs(x - centre) <= 2 and direction != "N":
            # Fish scales: each row offset by one, a bright crown on every scale.
            shifted = (x + (y // 2) % 2) % 2
            colour = (GOLD_HIGH if shifted == 0 else GOLD) if y % 2 == 0 else (GOLD if shifted == 0 else GOLD_DARK)
        elif abs(x - centre) == 3 and direction != "N":
            colour = GOLD_DARK
        else:
            colour = LACQUER_HIGH if y % 2 == 0 and x % 2 == 0 else LACQUER
        put(image, x, y, colour)

    # Lacquered tassets hanging from the waist to the knee, studded with gold.
    leg_left, _, leg_right, _ = bounds(legs)
    for y in range(bottom - 2, bottom + 4):
        for x in range(leg_left - 1, leg_right + 2):
            colour = LACQUER
            if y == bottom + 3:
                colour = GOLD
            elif y % 2 == 0 and x % 2 == 0:
                colour = GOLD_DARK
            if direction in ("S", "N") and x == centre and y > bottom:
                colour = LACQUER_DARK
            put(image, x, y, colour)

    # Purple sash with a gold buckle.
    sash = bottom - 4
    for x, y in torso:
        if y in (sash, sash + 1):
            put(image, x, y, PURPLE_HIGH if y == sash else PURPLE)
    if direction == "S":
        for x in range(centre - 1, centre + 2):
            put(image, x, sash, GOLD_HIGH)
            put(image, x, sash + 1, GOLD)
        put(image, centre, sash, RED)
        for y in range(sash + 2, sash + 7):
            put(image, centre + 3, y, PURPLE if y % 2 else PURPLE_HIGH)

    # Heart-protecting mirrors on the chest, or one great plate across the back.
    def mirror(cx, cy):
        # A round heart-protecting mirror, four pixels across with a bright rim and a shine.
        for dx, dy in ((0, -1), (1, -1), (-1, 0), (2, 0), (-1, 1), (2, 1), (0, 2), (1, 2)):
            put(image, cx + dx, cy + dy, GOLD)
        for dx, dy in ((0, 0), (1, 0), (0, 1), (1, 1)):
            put(image, cx + dx, cy + dy, GOLD_HIGH)
        put(image, cx, cy, GOLD_SHINE)

    if direction == "S":
        mirror(centre - 4, top + 3)
        mirror(centre + 3, top + 3)
    elif direction == "N":
        mirror(centre, top + 4)
    else:
        mirror(centre - 1, top + 4)

    # Sleeves: lacquer with gold scale dots, gold bracers at the wrist, flared beast-head pauldrons at the shoulder.
    for side in ("l_arm", "r_arm"):
        arm = masks[side][direction]
        if not arm:
            continue
        arm_left, arm_top, arm_right, arm_bottom = bounds(arm)
        for x, y in arm:
            colour = GOLD_DARK if (x + y) % 2 == 0 else LACQUER
            if y >= arm_bottom - 1:
                colour = GOLD
            put(image, x, y, colour)
        flare = -1 if arm_right <= centre else 1
        for y in range(arm_top - 1, arm_top + 3):
            for x in range(arm_left, arm_right + 1):
                put(image, x, y, GOLD_HIGH if y == arm_top - 1 else GOLD)
            put(image, (arm_left if flare < 0 else arm_right) + flare, y, GOLD_DARK)
        put(image, (arm_left + arm_right) // 2, arm_top + 1, RED)

    outline(image)
    return image


# ---------------------------------------------------------------- Phoenix-Wing Purple-Gold Crown

FEATHER_SOUTH = [(12, 3), (12, 2), (11, 1), (10, 0), (9, 0), (8, 1), (7, 1), (6, 2), (5, 3), (4, 4), (3, 5)]
FEATHER_EAST = [(15, 3), (15, 2), (14, 1), (13, 0), (12, 0), (11, 1), (10, 1), (9, 2), (8, 3), (7, 4), (6, 5)]


def feather(image, points, mirror=None, offset=(0, 0)):
    for index, (x, y) in enumerate(points):
        x, y = x + offset[0], y + offset[1]
        if mirror is not None:
            x = mirror - x
        colour = FEATHER_TIP if index >= len(points) - 2 else (FEATHER_LIGHT if (index // 2) % 2 == 0 else FEATHER_DARK)
        put(image, x, y, colour)


def crown(masks, direction):
    image = Image.new("RGBA", (32, 32))
    head = masks["head_m"][direction]
    left, top, right, _ = bounds(head)
    centre = (left + right) // 2

    if direction in ("S", "N"):
        feather(image, FEATHER_SOUTH)
        feather(image, FEATHER_SOUTH, mirror=left + right)
    else:
        mirror = None if direction == "E" else 31
        feather(image, FEATHER_EAST, mirror=mirror)
        feather(image, FEATHER_EAST, mirror=mirror, offset=(2, 1))

    # Purple cap under a gold band, with a jewelled crest at the front.
    for x, y in head:
        if y == top or y == top + 1:
            put(image, x, y, PURPLE_HIGH if y == top else PURPLE)
        elif y == top + 2:
            put(image, x, y, GOLD_HIGH if x % 2 == 0 else GOLD)
    if direction == "S":
        put(image, centre, top - 1, GOLD_HIGH)
        put(image, centre, top, GOLD)
        put(image, centre, top + 1, RED)
    elif direction in ("E", "W"):
        front = right if direction == "E" else left
        put(image, front, top + 1, GOLD_HIGH)

    outline(image, factor=0.55)
    return image


# ---------------------------------------------------------------- Lotus-Silk Cloud-Stepping Boots

def boots(masks, direction):
    image = Image.new("RGBA", (32, 32))
    for foot, leg in (("l_foot", "l_leg"), ("r_foot", "r_leg")):
        pixels = masks[foot][direction] | {(x, y) for x, y in masks[leg][direction] if y >= 26}
        if not pixels:
            continue
        left, top, right, bottom = bounds(pixels)
        for x, y in pixels:
            colour = LOTUS if (x + y) % 3 else LOTUS_DARK
            if y == top:
                colour = GOLD
            put(image, x, y, colour)
        # Upturned cloud toes.
        if direction == "S" or direction == "N":
            put(image, (left + right) // 2, bottom, CLOUD)
            put(image, (left + right) // 2 + 1, bottom, CLOUD)
        else:
            toe = right if direction == "E" else left
            put(image, toe, bottom - 1, CLOUD)
            put(image, toe, bottom, CLOUD)
    outline(image, factor=0.6)
    return image


# ---------------------------------------------------------------- Soft Hedgehog Armor

def hedgehog(masks, direction):
    image = Image.new("RGBA", (32, 32))
    torso = masks["torso_m"][direction]
    for x, y in torso:
        colour = SILK_SHEEN if (x - y) % 4 == 0 else SILK_BLACK
        if x % 3 == 0 and y % 3 == 1:
            colour = BARB
        put(image, x, y, colour)
    outline(image, factor=0.5)
    # Barbs poke out through the silhouette.
    left, top, right, bottom = bounds(torso)
    for y in range(top + 2, bottom, 3):
        if (left - 1, y) not in torso:
            put(image, left - 1, y, BARB)
        if (right + 1, y) not in torso:
            put(image, right + 1, y, BARB)
    for x in range(left + 1, right, 3):
        put(image, x, top - 1 if direction != "S" else top, BARB_DARK)
    return image


# ---------------------------------------------------------------- Golden Silk Vest

def silk_vest(masks, direction):
    image = Image.new("RGBA", (32, 32))
    torso = masks["torso_m"][direction]
    left, top, right, bottom = bounds(torso)
    centre = (left + right) // 2
    for x, y in torso:
        if y > bottom - 2:
            continue
        colour = GOLD if (x + y) % 2 == 0 else (214, 166, 62)
        if y == top or x in (left, right):
            colour = GOLD_DARK
        put(image, x, y, colour)
    if direction == "S":
        for y in range(top + 1, bottom - 2):
            put(image, centre, y, GOLD_DARK)
        for y in (top + 3, top + 6, top + 9):
            put(image, centre, y, JADE)
    outline(image, factor=0.5)
    return image


# ---------------------------------------------------------------- Writing the RSIs

def sheet(masks, painter):
    image = Image.new("RGBA", (64, 64))
    for direction, (ox, oy) in FRAMES.items():
        image.paste(painter(masks, direction), (ox, oy))
    return image


def icon_from(frame):
    """Centre a painted south-facing frame as the item's icon."""
    box = frame.getbbox()
    piece = frame.crop(box)
    icon = Image.new("RGBA", (32, 32))
    icon.paste(piece, ((32 - piece.width) // 2, (32 - piece.height) // 2))
    return icon


def crown_icon():
    """The crown on its own, larger than it sits on a head, with its two long plumes sweeping out."""
    icon = Image.new("RGBA", (32, 32))
    plume = [(13, 18), (12, 16), (11, 14), (10, 12), (9, 10), (8, 9), (7, 8), (6, 8), (5, 9), (4, 10), (3, 12), (2, 14), (1, 16)]
    for points in (plume, [(31 - x, y) for x, y in plume]):
        for index, (x, y) in enumerate(points):
            colour = FEATHER_TIP if index >= len(points) - 2 else (FEATHER_LIGHT if (index // 2) % 2 == 0 else FEATHER_DARK)
            put(icon, x, y, colour)
            put(icon, x, y + 1, colour)
    for y in range(18, 24):
        for x in range(11, 21):
            if y < 20:
                colour = PURPLE_HIGH if y == 18 else PURPLE
            elif y < 22:
                colour = GOLD_HIGH if x % 2 == 0 else GOLD
            else:
                colour = GOLD_DARK
            put(icon, x, y, colour)
    for x, y in ((15, 15), (16, 15), (15, 16), (16, 16), (15, 17), (16, 17)):
        put(icon, x, y, GOLD_HIGH)
    put(icon, 15, 19, RED)
    put(icon, 16, 19, RED)
    outline(icon, factor=0.55)
    return icon


def scaled_icon(frame, factor):
    piece = frame.crop(frame.getbbox())
    piece = piece.resize((piece.width * factor, piece.height * factor), Image.NEAREST)
    icon = Image.new("RGBA", (32, 32))
    icon.paste(piece, ((32 - piece.width) // 2, (32 - piece.height) // 2))
    return icon


def held(masks, colours):
    """A small folded bundle in the hand, in both hands' sheets."""
    sheets = {}
    for state, hand in (("inhand-left", "l_hand"), ("inhand-right", "r_hand")):
        image = Image.new("RGBA", (64, 64))
        for direction, (ox, oy) in FRAMES.items():
            pixels = masks[hand][direction]
            if not pixels:
                continue
            left, top, right, bottom = bounds(pixels)
            for y in range(bottom - 2, bottom + 2):
                for x in range(left - 1, left + 3):
                    image.putpixel((ox + x, oy + y), colours[(x + y) % len(colours)] + (255,))
        sheets[state] = image
    return sheets


def write(name, masks, painter, slot_state, colours, icon=None):
    path = os.path.join(TEXTURES, name)
    os.makedirs(path, exist_ok=True)
    states = {"icon": icon or icon_from(painter(masks, "S")), slot_state: sheet(masks, painter)}
    states.update(held(masks, colours))
    for state, image in states.items():
        image.save(os.path.join(path, state + ".png"))
    meta = {
        "version": 1,
        "license": LICENSE,
        "copyright": COPYRIGHT,
        "size": {"x": 32, "y": 32},
        "states": [{"name": "icon"}] + [{"name": state, "directions": 4} for state in states if state != "icon"],
    }
    with open(os.path.join(path, "meta.json"), "w", encoding="utf-8") as file:
        json.dump(meta, file, indent=2)
        file.write("\n")


def main():
    masks = load_masks()
    write("golden_chain_mail.rsi", masks, chain_mail, "equipped-OUTERCLOTHING", [GOLD, GOLD_DARK, LACQUER])
    write("phoenix_crown.rsi", masks, crown, "equipped-HELMET", [PURPLE, GOLD, FEATHER_LIGHT], icon=crown_icon())
    write("cloud_stepping_boots.rsi", masks, boots, "equipped-FEET", [LOTUS, LOTUS_DARK, GOLD],
          icon=scaled_icon(boots(masks, "E"), 2))
    write("soft_hedgehog_armor.rsi", masks, hedgehog, "equipped-OUTERCLOTHING", [SILK_BLACK, BARB])
    write("golden_silk_vest.rsi", masks, silk_vest, "equipped-OUTERCLOTHING", [GOLD, GOLD_HIGH])


if __name__ == "__main__":
    main()
