#!/usr/bin/env python3
# KS14: added in this fork
"""
Hand-drawn legendary armor of the jianghu. Every pixel is placed by hand in the grids below (see pixelgrid.py),
fitted to the human body: torso columns 10-20 and rows 10-23 facing south, head rows 3-9, legs from row 23.

The look is weathered bronze and dark iron chased with old gold, deep red cloth and grey fur:

  - the Great Sage's regalia from Journey to the West: Golden Chain-Mail, Phoenix-Wing Purple-Gold Crown and
    Lotus-Silk Cloud-Stepping Boots;
  - Soft Hedgehog Armor (Legend of the Condor Heroes): black silk bristling with barbs;
  - the Golden Silk Vest (The Deer and the Cauldron): a woven treasure vest with jade clasps.

Run from the repository root (needs Pillow):

    python3 Tools/_KS14/cultivation_armor.py
"""

import json
import os
import sys

from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pixelgrid import flip, four_directions, frame, mirror_row  # noqa: E402

TEXTURES = "Resources/Textures/_KS14/Cultivation"
COPYRIGHT = "Hand-drawn for Klovnstation 14 in Tools/_KS14/cultivation_armor.py"
LICENSE = "CC-BY-SA-3.0"

PALETTE = {
    "K": (16, 11, 10),      # outline
    "k": (40, 34, 34),      # dark iron
    "i": (70, 62, 60),      # iron
    "I": (112, 102, 96),    # iron, lit
    "b": (92, 54, 22),      # dark bronze
    "B": (158, 100, 40),    # bronze
    "g": (204, 152, 54),    # old gold
    "G": (248, 210, 116),   # gold, lit
    "w": (255, 246, 214),   # shine
    "r": (100, 16, 20),     # dark red cloth
    "R": (172, 34, 32),     # red cloth
    "f": (88, 76, 66),      # fur, shadow
    "F": (168, 156, 138),   # fur
    "p": (66, 34, 86),      # dark purple
    "P": (118, 68, 148),    # purple
    "t": (64, 42, 30),      # leather
    "T": (104, 72, 48),     # leather, lit
    "s": (26, 24, 30),      # black silk
    "S": (58, 56, 66),      # silk sheen
    "x": (112, 114, 126),   # barb
    "X": (190, 194, 206),   # barb, lit
    "y": (180, 132, 44),    # woven gold, shadow
    "J": (78, 190, 146),    # jade
    "j": (30, 106, 80),     # jade, shadow
    "e": (230, 236, 244),   # cloud white
    "#": (255, 0, 255),     # placeholder: woven brocade, filled in by brocade()
}


def symmetric(halves):
    return [mirror_row(half) for half in halves]


def draw(rows, top, left):
    return frame(rows, PALETTE, top=top, left=left)


# ================================================================ Golden Chain-Mail

# Left halves, columns 6-15 (15 is the centre); rows from 10.
CHAIN_MAIL_SOUTH = symmetric([
    "...KfFfFFf",   # 10 grey fur mantle round the neck
    ".KbBBKFfFF",   # 11 bronze pauldrons
    "KbBGgBkgGg",   # 12 gold scale mail between iron edges
    "KBgwGBkgbB",   # 13 heart-protecting mirror
    "KbBgBbkbGw",   # 14
    ".KbbbKkbGg",   # 15
    ".KiIiKkgbB",   # 16 iron vambraces
    ".KgGgKkGgG",   # 17 gold wrist bands
    ".....KkgGg",   # 18
    ".....KrRRg",   # 19 red sash
    ".....KRrRG",   # 20
    "....KkIkrR",   # 21 lamellar tassets, red front flap
    "....KiBirR",   # 22 bronze rivets
    "....KkIkrR",   # 23
    "....KiBirR",   # 24
    "....KkIkrR",   # 25
    "....KgGgrR",   # 26 gold hem
    ".....KKKrR",   # 27
    "........Kr",   # 28 flap tip
])

CHAIN_MAIL_NORTH = symmetric([
    "...KfFfFFf",   # 10
    ".KbBBKFfFF",   # 11
    "KbBGgBkikI",   # 12 iron lamellae
    "KBgwGBkIbB",   # 13 great back plate
    "KbBgBbkbGg",   # 14
    ".KbbbKkbgG",   # 15
    ".KiIiKkIbB",   # 16
    ".KgGgKkikI",   # 17
    ".....KkIki",   # 18
    ".....KrRrR",   # 19
    ".....KRrRr",   # 20
    "....KkIkiI",   # 21
    "....KiBiIi",   # 22
    "....KkIkiI",   # 23
    "....KiBiIi",   # 24
    "....KkIkiI",   # 25
    "....KgGgGg",   # 26
    ".....KKKKK",   # 27
])

# Facing east, columns 9-20; the arm swings in front of the torso.
CHAIN_MAIL_EAST = [
    "...KFfFfFfK.",  # 10
    "..KbBBBbKfFK",  # 11
    "..KBGgGBKgGK",  # 12
    "..KbGwGbKkgK",  # 13
    "..KbBgBbKgGK",  # 14
    "...KbbbKkgGK",  # 15
    "...KiIiKkGgK",  # 16
    "...KgGgKkgGK",  # 17
    ".......KkgK.",  # 18
    ".......KrRGK",  # 19
    ".......KRrgK",  # 20
    "....KkIkiIrK",  # 21
    "....KiBiiBrK",  # 22
    "....KkIkiIrK",  # 23
    "....KiBiiBrK",  # 24
    "....KkIkiIrK",  # 25
    "....KgGgGgrK",  # 26
    ".....KKKKKrK",  # 27
    "..........K.",  # 28
]

CHAIN_MAIL_ICON = symmetric([
    "....KfFfFFf",   # 6
    "..KbBBKFfFF",   # 7
    ".KbBGgBkgGg",   # 8
    ".KBgwGBkgbB",   # 9
    ".KbBgBbkbGw",   # 10
    "..KbbbKkbGg",   # 11
    "..KiIiKkgbB",   # 12
    "..KgGgKkGgG",   # 13
    "......KkgGg",   # 14
    "......KrRRg",   # 15
    "......KRrRG",   # 16
    ".....KkIkrR",   # 17
    ".....KiBirR",   # 18
    ".....KkIkrR",   # 19
    ".....KiBirR",   # 20
    ".....KgGgrR",   # 21
    "......KKKrR",   # 22
    ".........Kr",   # 23
])


# ================================================================ Phoenix-Wing Purple-Gold Crown

# Two long pheasant plumes rise from the cap and sweep out and down, banded light and dark.
PLUME_SOUTH = [(12, 3), (11, 2), (10, 1), (9, 0), (8, 0), (7, 0), (6, 1), (5, 1), (4, 2), (3, 3), (2, 4), (1, 5)]
PLUME_EAST = [(14, 3), (13, 2), (12, 1), (11, 0), (10, 0), (9, 0), (8, 1), (7, 1), (6, 2), (5, 3), (4, 4), (3, 5)]


def plume(image, points, mirror=None, offset=(0, 0)):
    for index, (x, y) in enumerate(points):
        x, y = x + offset[0], y + offset[1]
        if mirror is not None:
            x = mirror - x
        if index >= len(points) - 2:
            colour = PALETTE["K"]
        else:
            colour = PALETTE["F"] if (index // 2) % 2 == 0 else PALETTE["t"]
        if 0 <= x < 32 and 0 <= y < 32 and image.getpixel((x, y))[3] == 0:
            image.putpixel((x, y), colour + (255,))


# Left halves, columns 9-15; rows from 1. Gold phoenix wings flare up either side of a jewelled crest.
CROWN_SOUTH = symmetric([
    ".G....G",   # 1 wing tips, crest
    ".gG..KG",   # 2
    "..KgPPw",   # 3 purple cap, jewel
    "..KpPPR",   # 4
    "..KgGgG",   # 5 gold band
])

CROWN_NORTH = symmetric([
    ".G.....",   # 1
    ".gG....",   # 2
    "..KgPPP",   # 3
    "..KpPpP",   # 4
    "..KgGgG",   # 5
])

# Facing east, columns 11-21.
CROWN_EAST = [
    "...G.....G.",   # 1
    "...gG...KgK",   # 2
    "...KgPPPPw.",   # 3
    "...KpPPPRK.",   # 4
    "...KgGgGgK.",   # 5
]


def crown_sheet():
    south = draw(CROWN_SOUTH, top=1, left=9)
    plume(south, PLUME_SOUTH)
    plume(south, PLUME_SOUTH, mirror=30)
    north = draw(CROWN_NORTH, top=1, left=9)
    plume(north, PLUME_SOUTH)
    plume(north, PLUME_SOUTH, mirror=30)
    east = draw(CROWN_EAST, top=1, left=11)
    plume(east, PLUME_EAST)
    plume(east, PLUME_EAST, offset=(2, 1))
    return four_directions(south, north, east)


CROWN_ICON = symmetric([
    "...........G",   # 9 crest
    "...G......KG",   # 10
    "...gG....Kgw",   # 11 wings
    "....gG..KgGg",   # 12
    ".....KgPPPPw",   # 13 cap
    ".....KpPPPPR",   # 14
    ".....KpPpPPP",   # 15
    ".....KgGgGgG",   # 16 band
    ".....KbBbBbB",   # 17
    "......KKKKKK",   # 18
])


def crown_icon():
    icon = draw(CROWN_ICON, top=9, left=4)
    long_plume = [(10, 13), (9, 11), (8, 9), (7, 7), (6, 6), (5, 5), (4, 5), (3, 6), (2, 7), (1, 9), (1, 11), (0, 13)]
    for points in (long_plume, [(31 - x, y) for x, y in long_plume]):
        for index, (x, y) in enumerate(points):
            colour = PALETTE["K"] if index >= len(points) - 2 else (PALETTE["F"] if (index // 2) % 2 == 0 else PALETTE["t"])
            for dy in (0, 1):
                if image_empty(icon, x, y + dy):
                    icon.putpixel((x, y + dy), colour + (255,))
    return icon


def image_empty(image, x, y):
    return 0 <= x < 32 and 0 <= y < 32 and image.getpixel((x, y))[3] == 0


# ================================================================ Lotus-Silk Cloud-Stepping Boots

# Left boot, columns 8-15, mirrored across the gap between the feet; rows from 25.
BOOTS_SOUTH = [
    "..KFfFfK",   # 25 fur cuff
    "..KbBgBK",   # 26 bronze greaves chased with gold
    "..KBGBbK",   # 27
    "..KbgbBK",   # 28
    ".KtTtTtK",   # 29 leather foot
    ".KTtTtTK",   # 30
    "KeKKKKKK",   # 31 upturned cloud toe
]

BOOTS_NORTH = [
    "..KFfFfK",
    "..KbBbBK",
    "..KBbBbK",
    "..KbBbBK",
    ".KtTtTtK",
    ".KTtTtTK",
    ".KKKKKKK",
]

# Facing east, columns 12-21: the near boot over the far one, toes curling up at the front.
BOOTS_EAST = [
    ".KFfFfK...",   # 25
    ".KbBgBK...",   # 26
    ".KBGBbK...",   # 27
    ".KbgbBK...",   # 28
    ".KtTtTtTKe",   # 29
    ".KTtTtTtKe",   # 30
    ".KKKKKKKKK",   # 31
]


def boots_frame(rows):
    return draw([row + row[::-1] for row in rows], top=25, left=8)


BOOTS_ICON = [
    "...KFfFfK.....",
    "...KbBgBK.....",
    "...KBGBbK.....",
    "...KbgbBK.....",
    "...KBGBbK.....",
    "...KbgbBK.....",
    "...KtTtTtTTKe.",
    "...KTtTtTtTKee",
    "...KtTtTtTtTKe",
    "...KKKKKKKKKK.",
]


# ================================================================ Soft Hedgehog Armor

# Dark silk lamellae; barbs are two-pixel thorns (lit tip, shadowed root) set in rows, and longer spines bristle
# from the shoulders and flanks. Left halves, columns 8-15; rows from 9.
HEDGEHOG_SOUTH = symmetric([
    "...X..X.",   # 9  shoulder spines
    "...xKSSS",   # 10 collar
    "XxKsSsSs",   # 11 flank spine
    "..KXssXs",   # 12 barbs
    "..KxSsxS",   # 13
    "XxKsSsSs",   # 14
    "..KsXssX",   # 15
    "..KSxsSx",   # 16
    "XxKsSsSs",   # 17
    "..KXssXs",   # 18
    "..KxSsxS",   # 19
    "..KsSsSs",   # 20
    "...KKKKK",   # 21
])

HEDGEHOG_NORTH = symmetric([
    "...X..X.",
    "...xKSSS",
    "XxKsXsSX",
    "..KsxsSx",
    "..KSsSsS",
    "XxKXssXs",
    "..KxSsxS",
    "..KsSsSs",
    "XxKsXsSX",
    "..KsxsSx",
    "..KSsSsS",
    "..KsSsSs",
    "...KKKKK",
])

# Facing east, columns 8-20.
HEDGEHOG_EAST = [
    "......X..X...",   # 9
    "......xKSSSK.",   # 10
    "..xXKsSsSsKXx",   # 11
    "....KXssXsK..",   # 12
    "....KxSsxSK..",   # 13
    "..xXKsSsSsKXx",   # 14
    "....KsXssXK..",   # 15
    "....KSxsSxK..",   # 16
    "..xXKsSsSsKXx",   # 17
    "....KXssXsK..",   # 18
    "....KxSsxSK..",   # 19
    "....KsSsSsK..",   # 20
    ".....KKKKK...",   # 21
]


# ================================================================ Golden Silk Vest

# '#' is woven gold brocade, filled with a diagonal twill below; a gold cloud emblem sits on the breast and jade
# clasps close the front. Left halves, columns 9-15; rows from 10.
SILK_VEST_SOUTH = symmetric([
    "..KbGK.",   # 10 open collar
    "..Kg##K",   # 11
    "..K###J",   # 12 jade clasp
    "..K#wGb",   # 13 cloud emblem
    "..K#GgG",   # 14
    "..K###J",   # 15
    "..K###b",   # 16
    "..K###J",   # 17
    "..K###b",   # 18
    "..Kgggg",   # 19 gold trim
    "..KbbbK",   # 20 hem
    "...KKK.",   # 21
])

SILK_VEST_NORTH = symmetric([
    "..KbGGG",
    "..Kg###",
    "..K####",
    "..K#wGG",
    "..K#Ggg",
    "..K####",
    "..K####",
    "..K####",
    "..K####",
    "..Kgggg",
    "..KbbbB",
    "...KKKK",
])

SILK_VEST_EAST = [
    "..KbGGK...",   # 10
    "..Kg###K..",   # 11
    "..K####J..",   # 12
    "..K##wGj..",   # 13
    "..K##GgJ..",   # 14
    "..K####j..",   # 15
    "..K####J..",   # 16
    "..K####j..",   # 17
    "..K####J..",   # 18
    "..KggggK..",   # 19
    "..KbbbbK..",   # 20
    "...KKKK...",   # 21
]


def brocade(image):
    """Fill every '#' placeholder pixel with a diagonal gold twill."""
    pixels = image.load()
    marker = PALETTE["#"] + (255,)
    for y in range(image.height):
        for x in range(image.width):
            if pixels[x, y] != marker:
                continue
            step = (x + y) % 3
            colour = PALETTE["G"] if step == 0 else (PALETTE["g"] if step == 1 else PALETTE["y"])
            pixels[x, y] = colour + (255,)
    return image


# ================================================================ Writing the RSIs

def centred(image):
    box = image.getbbox()
    piece = image.crop(box)
    icon = Image.new("RGBA", (32, 32))
    icon.paste(piece, ((32 - piece.width) // 2, (32 - piece.height) // 2))
    return icon


# Hands: right hand at columns 7-10 facing south, left hand at 20-23; rows 18-21.
HAND_SPOTS = {
    "inhand-right": {"S": (7, 18), "N": (20, 18), "E": (12, 18), "W": (16, 18)},
    "inhand-left": {"S": (20, 18), "N": (7, 18), "E": (17, 18), "W": (11, 18)},
}
FRAME_OFFSETS = {"S": (0, 0), "N": (32, 0), "E": (0, 32), "W": (32, 32)}


def held_sheets(bundle):
    """The folded item held in each hand: a small hand-drawn bundle placed at the hand."""
    sheets = {}
    for state, spots in HAND_SPOTS.items():
        image = Image.new("RGBA", (64, 64))
        for direction, (x, y) in spots.items():
            ox, oy = FRAME_OFFSETS[direction]
            image.alpha_composite(draw(bundle, top=0, left=0).crop((0, 0, 32, 32)), (ox + x - 1, oy + y))
        sheets[state] = image
    return sheets


def write(name, slot_state, sheet, icon, bundle):
    path = os.path.join(TEXTURES, name)
    os.makedirs(path, exist_ok=True)
    for old in os.listdir(path):
        os.remove(os.path.join(path, old))
    states = {"icon": icon, slot_state: sheet}
    states.update(held_sheets(bundle))
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
    write("golden_chain_mail.rsi", "equipped-OUTERCLOTHING",
          four_directions(draw(CHAIN_MAIL_SOUTH, 10, 6), draw(CHAIN_MAIL_NORTH, 10, 6), draw(CHAIN_MAIL_EAST, 10, 9)),
          draw(CHAIN_MAIL_ICON, top=6, left=5),
          ["KgBK", "KGgK", "KrRK", "KKKK"])
    write("phoenix_crown.rsi", "equipped-HELMET", crown_sheet(), crown_icon(), ["KPgK", "KgGK", "KKKK"])
    write("cloud_stepping_boots.rsi", "equipped-FEET",
          four_directions(boots_frame(BOOTS_SOUTH), boots_frame(BOOTS_NORTH), draw(BOOTS_EAST, top=25, left=12)),
          centred(draw(BOOTS_ICON, top=10, left=6)),
          ["KbBK", "KtTK", "KKKK"])
    write("soft_hedgehog_armor.rsi", "equipped-OUTERCLOTHING",
          four_directions(draw(HEDGEHOG_SOUTH, 9, 8), draw(HEDGEHOG_NORTH, 9, 8), draw(HEDGEHOG_EAST, 9, 8)),
          centred(draw(HEDGEHOG_SOUTH, 9, 8)),
          ["KsxK", "KxsK", "KKKK"])
    write("golden_silk_vest.rsi", "equipped-OUTERCLOTHING",
          four_directions(brocade(draw(SILK_VEST_SOUTH, 10, 9)), brocade(draw(SILK_VEST_NORTH, 10, 9)),
                          brocade(draw(SILK_VEST_EAST, 10, 10))),
          centred(brocade(draw(SILK_VEST_SOUTH, 10, 9))),
          ["KGyK", "KyJK", "KKKK"])


if __name__ == "__main__":
    main()
