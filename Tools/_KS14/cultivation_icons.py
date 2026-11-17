#!/usr/bin/env python3
# KS14: added in this fork
"""
Generates the cultivation RSIs: technique medallions, manuals, and the qi / internal injury alerts.
Armor is drawn separately by cultivation_armor.py.

Each medallion is a coloured disc with a bevelled rim and a single Chinese glyph, so every technique reads at a
glance at 32x32. Run from the repository root:

    python3 Tools/_KS14/cultivation_icons.py

Needs Pillow and a CJK font (WenQuanYi Zen Hei by default; pass --font to use another).
"""

import argparse
import json
import math
import os
import shutil

from PIL import Image, ImageDraw, ImageFilter, ImageFont

TEXTURES = "Resources/Textures/_KS14/Cultivation"
COPYRIGHT = "Generated for Klovnstation 14 by Tools/_KS14/cultivation_icons.py"
LICENSE = "CC-BY-SA-3.0"

# state: (glyph, face colour, rim colour)
PALETTE = {
    "gold": ((236, 196, 92), (128, 86, 24)),
    "silver": ((206, 212, 226), (92, 98, 118)),
    "red": ((222, 84, 54), (110, 28, 18)),
    "orange": ((240, 146, 60), (122, 60, 16)),
    "blue": ((84, 150, 226), (24, 56, 110)),
    "teal": ((72, 196, 196), (18, 86, 92)),
    "green": ((110, 196, 96), (34, 88, 30)),
    "jade": ((92, 186, 150), (26, 84, 64)),
    "brown": ((176, 128, 78), (82, 54, 26)),
    "tan": ((204, 170, 116), (100, 76, 40)),
    "purple": ((150, 98, 214), (60, 30, 104)),
    "crimson": ((176, 30, 58), (70, 8, 20)),
    "violet": ((118, 64, 160), (44, 16, 70)),
}

ACTIONS = {
    "meditate": ("禅", "jade"),
    "name_heir": ("嗣", "gold"),
    "nine_yin_claw": ("爪", "violet"),
    "wumu_command": ("军", "crimson"),
    "ring_speak": ("语", "tan"),
    "lend_qi": ("借", "gold"),
    "breakthrough": ("破", "gold"),
    "flying_sword": ("剑", "silver"),
    "sword_rain": ("雨", "silver"),
    "burning_palm": ("掌", "red"),
    "sea_of_flames": ("炎", "orange"),
    "still_water": ("静", "blue"),
    "tidal_surge": ("潮", "teal"),
    "spring_mending": ("春", "green"),
    "myriad_spring": ("万", "jade"),
    "rooted_stance": ("山", "brown"),
    "mountain_collapse": ("崩", "tan"),
}

# Martial arts from the novels.
ACTIONS.update({
    "dragon_palm": ("龙", "gold"),
    "six_meridians": ("脉", "teal"),
    "northern_darkness": ("冥", "violet"),
    "star_absorbing": ("吸", "crimson"),
    "great_shift": ("移", "purple"),
    "golden_bell": ("钟", "gold"),
    "graceful_steps": ("波", "blue"),
    "ladder_cloud": ("云", "silver"),
    "dugu_counter": ("独", "silver"),
})

# Body Molding arts.
ACTIONS.update({
    "body_forge": ("锻", "orange"),
    "body_tribulation": ("劫", "crimson"),
    "body_breath": ("息", "jade"),
    "body_iron_shirt": ("铁", "silver"),
    "body_leap": ("跃", "tan"),
    "body_stomp": ("震", "brown"),
    "body_fist": ("拳", "red"),
    "body_hundred_fists": ("百", "orange"),
    "body_bone_setting": ("骨", "tan"),
    "body_thunderclap": ("雷", "gold"),
    "body_mountain": ("岳", "brown"),
    "body_disciple": ("徒", "jade"),
    "body_remold": ("塑", "tan"),
    "body_bull_charge": ("牛", "red"),
    "body_blood_boil": ("沸", "crimson"),
    "body_falling_mountain": ("坠", "brown"),
    "body_hurl": ("掷", "tan"),
    "body_sky_palm": ("裂", "teal"),
    "body_vajra": ("刚", "gold"),
    "body_quake": ("撼", "brown"),
    "body_roar": ("吼", "red"),
})

# Universal, law and combination techniques.
ACTIONS.update({
    "spiritual_sense": ("识", "jade"),
    "empty_palm": ("空", "silver"),
    "qinggong": ("轻", "blue"),
    "void_step": ("虚", "violet"),
    "realm_pressure": ("压", "purple"),
    "acupoint": ("穴", "teal"),
    "write_talisman": ("符", "gold"),
    "transmit_law": ("传", "jade"),
    "concealment": ("隐", "violet"),
    "forced_circulation": ("逆", "crimson"),
    "forget_law": ("忘", "tan"),
    "gathering_array": ("聚", "jade"),
    "barrier_array": ("阵", "gold"),
    "alarm_array": ("警", "orange"),
    "body_outside_body": ("婴", "gold"),
    "return_to_body": ("归", "gold"),
    "bind_artifact": ("契", "silver"),
    "artifact_strike": ("击", "silver"),
    "sword_riding": ("御", "silver"),
    "sword_formation": ("列", "silver"),
    "calm_heart": ("宁", "blue"),
    "turtle_breathing": ("龟", "teal"),
    "mirror_lake": ("镜", "blue"),
    "kindle": ("燃", "orange"),
    "furnace_burst": ("炉", "red"),
    "burning_blood": ("血", "crimson"),
    "golden_bell_qi": ("罩", "gold"),
    "dharma_idol": ("像", "gold"),
    "buddha_palm": ("佛", "gold"),
    "verdant_growth": ("生", "green"),
    "binding_vines": ("藤", "green"),
    "steam_veil": ("雾", "teal"),
    "thousand_thorns": ("刺", "green"),
    "molten_step": ("熔", "orange"),
    "mud_prison": ("泥", "brown"),
    "beast_contract": ("兽", "brown"),
    "summon_beast": ("召", "jade"),
})

# The jianghu.
ACTIONS.update({
    "duel": ("决", "red"),
    "yield": ("让", "tan"),
    "found_sect": ("宗", "gold"),
    "recall_teachings": ("忆", "tan"),
    "sect_transmission": ("讯", "jade"),
    "declare_rivalry": ("敌", "red"),
})

# The Demonic Path.
ACTIONS.update({
    "devouring_art": ("噬", "crimson"),
    "gu_worm": ("蛊", "violet"),
    "stir_gu": ("搅", "violet"),
    "soul_search": ("搜", "purple"),
    "corpse_puppet": ("尸", "violet"),
    "blood_escape": ("遁", "crimson"),
    "exploding_heart": ("爆", "crimson"),
    "transmit_demonic": ("魔", "crimson"),
})

ART_MANUALS = {
    "body_primer": ("铜", "orange"),
    "body_molding_art": ("塑", "brown"),
    "art_dragon_palm": ("龙", "gold"),
    "art_six_meridians": ("脉", "teal"),
    "art_northern_darkness": ("冥", "violet"),
    "art_star_absorbing": ("吸", "crimson"),
    "art_great_shift": ("移", "purple"),
    "art_nine_yang": ("阳", "orange"),
    "art_golden_bell": ("钟", "gold"),
    "art_graceful_steps": ("波", "blue"),
    "art_ladder_cloud": ("云", "silver"),
    "art_dugu": ("独", "silver"),
    "art_muscle_tendon": ("筋", "brown"),
    "art_drunken_fist": ("醉", "orange"),
    "art_eagle_claw": ("鹰", "tan"),
    "art_wing_chun": ("咏", "blue"),
    "art_nine_yin": ("阴", "violet"),
    "art_wumu": ("岳", "crimson"),
}

MANUALS = {
    "art_dragon_palm": ("龙", "gold"),
    "art_six_meridians": ("脉", "teal"),
    "art_northern_darkness": ("冥", "violet"),
    "art_star_absorbing": ("吸", "crimson"),
    "art_great_shift": ("移", "purple"),
    "art_nine_yang": ("阳", "orange"),
    "art_golden_bell": ("钟", "gold"),
    "art_graceful_steps": ("波", "blue"),
    "art_ladder_cloud": ("云", "silver"),
    "art_dugu": ("独", "silver"),
    "art_muscle_tendon": ("筋", "brown"),
    "art_drunken_fist": ("醉", "orange"),
    "art_eagle_claw": ("鹰", "tan"),
    "art_wing_chun": ("咏", "blue"),
}

# Legendary armor: the vanilla vest, gradient-mapped onto a new palette (dark, mid, light).
ARMORS = {
    "soft_hedgehog_armor.rsi": ((40, 26, 18), (112, 76, 44), (196, 150, 96)),
    "golden_silk_vest.rsi": ((92, 58, 10), (206, 160, 52), (255, 236, 150)),
}
VEST = "Resources/Textures/Clothing/OuterClothing/Vests/vest.rsi"

MANUALS = {
    "manual_metal": ("金", "silver"),
    "manual_wood": ("木", "green"),
    "manual_water": ("水", "blue"),
    "manual_fire": ("火", "red"),
    "manual_earth": ("土", "brown"),
    "manual_demonic": ("魔", "crimson"),
}


def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


def glyph_layer(glyph, font_path, size, box, threshold=110):
    """The glyph, white with a dark outline, centred in a box of the given size."""
    font = ImageFont.truetype(font_path, size)
    layer = Image.new("L", (box, box), 0)
    draw = ImageDraw.Draw(layer)
    left, top, right, bottom = draw.textbbox((0, 0), glyph, font=font)
    x = (box - (right - left)) / 2 - left
    y = (box - (bottom - top)) / 2 - top
    draw.text((x, y), glyph, fill=255, font=font)
    # Threshold so the glyph stays crisp pixel art rather than a blur.
    return layer.point(lambda v: 255 if v >= threshold else 0)


def medallion(glyph, colours, font_path):
    """
    An action button: a cast medallion with a dark outline, a studded rim and a bevelled inner edge, faint auspicious
    clouds swirling on its face, and the art's character raised on it with a drop shadow and a gloss over the top.
    """
    face, rim = colours
    size = 32
    image = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    pixels = image.load()
    centre = (size - 1) / 2
    outline, outer, inner = 15.4, 14.4, 11.6
    dark_rim = lerp(rim, (0, 0, 0), 0.55)
    for y in range(size):
        for x in range(size):
            dx, dy = x - centre, y - centre
            distance = math.hypot(dx, dy)
            if distance > outline:
                continue
            light = max(0.0, min(1.0, 0.5 - (dx + dy) / (2 * outer)))
            if distance > outer:
                colour = lerp(dark_rim, (0, 0, 0), 0.3)
            elif distance > inner + 0.9:
                colour = lerp(rim, lerp(rim, face, 0.6), light)
            elif distance > inner:
                # The bevel: lit on the bottom right inside the rim, shaded on the top left.
                colour = lerp(lerp(face, (255, 255, 255), 0.35), dark_rim, light)
            else:
                # A soft radial gradient, brightest a little up and left of the middle.
                glow = max(0.0, 1 - math.hypot(dx + 3, dy + 3) / (inner + 4))
                colour = lerp(lerp(face, rim, 0.45), lerp(face, (255, 255, 255), 0.18), glow * 0.7 + light * 0.3)
                # Faint auspicious clouds: curls of slightly darker face.
                swirl = math.sin(distance * 1.25 + math.atan2(dy, dx) * 2)
                if swirl > 0.82:
                    colour = lerp(colour, rim, 0.22)
            pixels[x, y] = colour + (255,)

    # Eight studs round the rim.
    draw = ImageDraw.Draw(image)
    for index in range(8):
        angle = index / 8 * math.tau + math.pi / 8
        sx, sy = centre + math.cos(angle) * 13.0, centre + math.sin(angle) * 13.0
        draw.point((round(sx), round(sy)), fill=lerp(face, (255, 255, 255), 0.55) + (255,))
        draw.point((round(sx) + 1, round(sy) + 1), fill=dark_rim + (255,))

    mask = glyph_layer(glyph, font_path, 18, size)
    shadow = Image.new("L", (size, size), 0)
    shadow.paste(mask.filter(ImageFilter.MaxFilter(3)), (1, 1))
    image.paste(Image.new("RGBA", (size, size), lerp(dark_rim, (0, 0, 0), 0.4) + (200,)), (0, 0), shadow)
    outline_mask = mask.filter(ImageFilter.MaxFilter(3))
    image.paste(Image.new("RGBA", (size, size), lerp(rim, (0, 0, 0), 0.5) + (255,)), (0, 0), outline_mask)
    image.paste(Image.new("RGBA", (size, size), (252, 248, 232, 255)), (0, 0), mask)

    # A gloss over the top left of the face.
    gloss = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    ImageDraw.Draw(gloss).arc((6, 6, 25, 25), start=190, end=260, fill=(255, 255, 255, 140), width=2)
    image.alpha_composite(gloss)
    return image


def manual(glyph, colours, font_path):
    """
    A bound manual in the SS13 manuals' style: an outlined cover in the art's colour, page edges showing on the right,
    a dark spine with gold bands, gold caps on the corners, and a recessed parchment panel with the art's character.
    """
    face, rim = colours
    size = 32
    image = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    outline = lerp(rim, (0, 0, 0), 0.6) + (255,)
    gold, gold_dark = (232, 192, 88, 255), (150, 104, 30, 255)
    # Page block peeking out on the right and bottom.
    draw.rectangle((9, 4, 27, 29), fill=outline)
    draw.rectangle((10, 5, 26, 28), fill=(236, 226, 196, 255))
    for y in range(7, 28, 2):
        draw.line((24, y, 26, y), fill=(196, 182, 150, 255))
    # The cover.
    draw.rectangle((5, 3, 25, 28), fill=outline)
    for y in range(4, 28):
        light = 1 - (y - 4) / 24
        draw.line((6, y, 24, y), fill=lerp(lerp(face, rim, 0.35), face, light * 0.8) + (255,))
    # Spine with gold bands.
    draw.rectangle((6, 4, 8, 27), fill=lerp(rim, (0, 0, 0), 0.25) + (255,))
    for y in (7, 15, 24):
        draw.line((6, y, 8, y), fill=gold)
    # Gold corner caps.
    for x, y in ((22, 4), (22, 25)):
        draw.rectangle((x, y, x + 2, y + 2), fill=gold)
        draw.point((x, y + 2 if y < 10 else y), fill=gold_dark)
    # A recessed panel: dark on top and left, lit on the bottom and right, parchment inside.
    draw.rectangle((11, 7, 21, 24), fill=lerp(rim, (0, 0, 0), 0.35) + (255,))
    draw.line((12, 24, 21, 24), fill=lerp(face, (255, 255, 255), 0.4) + (255,))
    draw.line((21, 8, 21, 24), fill=lerp(face, (255, 255, 255), 0.4) + (255,))
    draw.rectangle((12, 8, 20, 23), fill=(240, 230, 200, 255))
    mask = glyph_layer(glyph, font_path, 9, size, threshold=60)
    shifted = Image.new("L", (size, size), 0)
    shifted.paste(mask, (0, 0))
    image.paste(Image.new("RGBA", (size, size), (36, 20, 14, 255)), (0, 0), shifted)
    return image

def qi_alert():
    """The qi meter's base: a dark plate with a small taiji, leaving room for the digits."""
    image = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    draw.rounded_rectangle((1, 1, 30, 30), radius=5, fill=(24, 40, 44, 230), outline=(92, 186, 150, 255))
    # A small taiji above the digits.
    light, dark = (152, 236, 200, 255), (24, 40, 44, 255)
    draw.ellipse((11, 3, 21, 13), fill=light, outline=(92, 186, 150, 255))
    draw.pieslice((11, 3, 21, 13), 90, 270, fill=dark)
    draw.ellipse((14, 3, 18, 8), fill=dark)
    draw.ellipse((14, 8, 18, 13), fill=light)
    draw.point((16, 5), fill=light)
    draw.point((16, 10), fill=dark)
    return image


def talisman(font_path):
    """A strip of yellow paper with a red seal glyph."""
    image = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    draw.rectangle((11, 3, 20, 28), fill=(232, 204, 92, 255), outline=(150, 112, 34, 255))
    mask = glyph_layer("符", font_path, 10, 32, threshold=60)
    image.paste(Image.new("RGBA", (32, 32), (186, 30, 30, 255)), (0, 0), mask)
    draw.line((13, 24, 18, 24), fill=(186, 30, 30, 255))
    return image


def jade_slip(font_path):
    """A slip of green jade with a carved glyph."""
    image = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    draw.rounded_rectangle((10, 4, 21, 27), radius=2, fill=(92, 186, 150, 255), outline=(26, 84, 64, 255))
    draw.line((12, 6, 12, 25), fill=(160, 230, 200, 255))
    mask = glyph_layer("传", font_path, 10, 32, threshold=60)
    image.paste(Image.new("RGBA", (32, 32), (26, 84, 64, 255)), (0, 0), mask)
    return image


def blood_slip(font_path):
    """A strip of yellowed bone written over in blood."""
    image = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    draw.rounded_rectangle((10, 4, 21, 27), radius=3, fill=(214, 200, 164, 255), outline=(110, 84, 56, 255))
    draw.line((12, 7, 12, 24), fill=(236, 228, 204, 255))
    mask = glyph_layer("魔", font_path, 10, 32, threshold=60)
    image.paste(Image.new("RGBA", (32, 32), (150, 16, 24, 255)), (0, 0), mask)
    # A drip running off the bottom.
    draw.line((18, 25, 18, 29), fill=(150, 16, 24, 255))
    return image


def sect_plaque(font_path):
    """A lacquered red board in a gilded frame, carrying the character for a sect."""
    image = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    # Two posts and a little roof over the board.
    draw.rectangle((7, 10, 9, 30), fill=(92, 44, 22, 255))
    draw.rectangle((22, 10, 24, 30), fill=(92, 44, 22, 255))
    draw.polygon([(4, 9), (16, 3), (27, 9), (24, 11), (7, 11)], fill=(40, 34, 32, 255), outline=(20, 16, 14, 255))
    draw.rectangle((9, 11, 22, 26), fill=(206, 160, 52, 255))
    draw.rectangle((10, 12, 21, 25), fill=(150, 28, 26, 255))
    mask = glyph_layer("宗", font_path, 10, 32, threshold=60)
    shifted = Image.new("L", (32, 32), 0)
    shifted.paste(mask, (0, 3))
    image.paste(Image.new("RGBA", (32, 32), (246, 214, 110, 255)), (0, 0), shifted)
    return image


def incense(lit):
    """Three sticks of incense tied with red thread; lit, their tips glow and smoke curls up."""
    image = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    for x in (13, 16, 19):
        draw.line((x, 9, x, 27), fill=(150, 98, 52, 255), width=2)
        draw.line((x, 22, x, 27), fill=(120, 30, 26, 255), width=2)
    draw.line((12, 19, 20, 19), fill=(206, 34, 34, 255), width=2)
    if lit:
        for x in (13, 16, 19):
            draw.rectangle((x, 7, x + 1, 9), fill=(255, 132, 40, 255))
        for index, (x, y) in enumerate(((14, 4), (17, 2), (20, 5), (15, 0))):
            draw.point((x, y), fill=(216, 208, 192, 200 - index * 30))
    return image


def wuxia_weapon(kind):
    """The ordinary wuxia weapons and hidden weapons, drawn diagonally like vanilla item icons."""
    image = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    steel, edge, dark = (186, 192, 204, 255), (236, 240, 246, 255), (70, 74, 86, 255)
    wood, red, gold = (122, 78, 40, 255), (186, 34, 34, 255), (214, 170, 60, 255)
    if kind == "meteor_hammer":
        draw.arc((6, 6, 26, 26), 200, 340, fill=(150, 120, 80, 255), width=1)
        draw.line((9, 17, 23, 17), fill=(150, 120, 80, 255))
        for cx, cy in ((7, 20), (25, 20)):
            draw.ellipse((cx - 4, cy - 4, cx + 4, cy + 4), fill=dark, outline=(30, 30, 36, 255))
            draw.point((cx - 1, cy - 2), fill=edge)
    elif kind == "rope_dart":
        draw.line((4, 28, 12, 18, 8, 12, 16, 8), fill=red, width=1)
        draw.polygon([(16, 8), (26, 3), (22, 11)], fill=steel, outline=dark)
        draw.line((17, 8, 24, 5), fill=edge)
    elif kind == "butterfly_sword":
        draw.polygon([(12, 20), (25, 5), (28, 6), (15, 22)], fill=steel, outline=dark)
        draw.line((14, 20, 26, 6), fill=edge)
        draw.arc((6, 18, 16, 28), 90, 270, fill=gold, width=2)
        draw.line((8, 26, 12, 22), fill=wood, width=3)
    elif kind == "iron_fan":
        for angle in range(200, 341, 20):
            radians = math.radians(angle)
            draw.line((16, 26, 16 + 13 * math.cos(radians), 26 + 13 * math.sin(radians)), fill=dark)
        draw.pieslice((3, 13, 29, 39), 200, 340, fill=(150, 30, 30, 230), outline=dark)
        draw.arc((3, 13, 29, 39), 200, 340, fill=edge)
        draw.ellipse((14, 24, 18, 28), fill=gold)
    elif kind == "bamboo_staff":
        draw.line((4, 28, 28, 4), fill=(150, 168, 72, 255), width=3)
        for t in (0.25, 0.5, 0.75):
            x, y = 4 + 24 * t, 28 - 24 * t
            draw.line((x - 2, y - 2, x + 2, y + 2), fill=(96, 112, 40, 255), width=1)
    elif kind == "sect_jian":
        draw.line((10, 22, 27, 5), fill=steel, width=2)
        draw.line((11, 21, 27, 5), fill=edge)
        draw.line((7, 19, 13, 25), fill=gold, width=2)
        draw.line((5, 27, 9, 23), fill=wood, width=2)
        draw.line((4, 28, 3, 31), fill=red, width=2)
    elif kind == "needles":
        for offset in (-3, 0, 3):
            draw.line((8 + offset, 26, 24 + offset, 6), fill=steel)
            draw.point((24 + offset, 6), fill=edge)
        draw.ellipse((5, 24, 9, 28), fill=red)
    elif kind == "flying_dagger":
        draw.polygon([(10, 22), (26, 5), (24, 10), (13, 23)], fill=steel, outline=dark)
        draw.line((11, 21, 25, 6), fill=edge)
        draw.line((6, 25, 11, 21), fill=wood, width=2)
        draw.line((5, 26, 2, 29), fill=red)
    elif kind == "smoke_pellet":
        draw.ellipse((10, 14, 22, 26), fill=(214, 200, 160, 255), outline=(120, 100, 70, 255))
        draw.line((16, 14, 16, 8), fill=(120, 100, 70, 255))
        draw.line((16, 8, 19, 6), fill=(120, 100, 70, 255))
        for x, y in ((12, 18), (19, 21), (15, 23)):
            draw.point((x, y), fill=(90, 80, 70, 255))
    return image


def floor_crack(seed):
    """A dark star of cracks where something hit the floor far too hard, with chipped rubble round the crater."""
    import random
    rng = random.Random(seed)
    image = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    dark, mid = (14, 12, 12), (46, 42, 40)
    centre = (16 + rng.randint(-2, 2), 16 + rng.randint(-2, 2))
    for branch in range(rng.randint(7, 10)):
        angle = branch * 2 * math.pi / 9 + rng.uniform(-0.3, 0.3)
        x, y = centre
        length = rng.randint(11, 17)
        for step in range(length):
            angle += rng.uniform(-0.45, 0.45)
            nx, ny = x + math.cos(angle) * 1.3, y + math.sin(angle) * 1.3
            width = 3 if step < 3 else (2 if step < 8 else 1)
            draw.line((x, y, nx, ny), fill=dark + (235 - step * 6,), width=width)
            if rng.random() < 0.25 and step > 2:
                fork = angle + rng.choice((-1, 1)) * rng.uniform(0.5, 1.0)
                draw.line((nx, ny, nx + math.cos(fork) * 5, ny + math.sin(fork) * 5), fill=dark + (170,))
            x, y = nx, ny
    radius = rng.randint(4, 6)
    draw.ellipse((centre[0] - radius, centre[1] - radius, centre[0] + radius, centre[1] + radius), fill=dark + (220,))
    draw.ellipse((centre[0] - radius + 2, centre[1] - radius + 2, centre[0] + radius - 2, centre[1] + radius - 2), fill=(6, 5, 5, 240))
    for _ in range(rng.randint(6, 10)):
        rx, ry = centre[0] + rng.randint(-9, 9), centre[1] + rng.randint(-9, 9)
        draw.rectangle((rx, ry, rx + 1, ry + rng.randint(0, 1)), fill=mid + (230,))
    return image


def qi_wall():
    image = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    draw.rectangle((0, 0, 31, 31), fill=(250, 214, 112, 90), outline=(250, 214, 112, 200))
    for y in range(3, 32, 6):
        draw.line((2, y, 29, y), fill=(255, 246, 206, 120))
    return image


def meter_plate(glyph, colour, font_path):
    """Base for a counter alert: a dark plate with a small glyph above the digits."""
    face, rim = PALETTE[colour]
    image = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    draw.rounded_rectangle((1, 1, 30, 30), radius=5, fill=(30, 26, 24, 230), outline=rim + (255,))
    mask = glyph_layer(glyph, font_path, 12, 32, threshold=70)
    shifted = Image.new("L", (32, 32), 0)
    shifted.paste(mask, (0, -8))
    image.paste(Image.new("RGBA", (32, 32), face + (255,)), (0, 0), shifted)
    return image


def injury_alert(stacks, font_path):
    image = medallion("伤", PALETTE["crimson"], font_path)
    draw = ImageDraw.Draw(image)
    for index in range(stacks):
        x = 6 + index * 8
        draw.rectangle((x, 26, x + 5, 30), fill=(240, 60, 60, 255), outline=(60, 0, 0, 255))
    return image


def write_rsi(path, states, extra_copyright=""):
    os.makedirs(path, exist_ok=True)
    for name, image in states.items():
        image.save(os.path.join(path, name + ".png"))
    meta = {
        "version": 1,
        "license": LICENSE,
        "copyright": COPYRIGHT + extra_copyright,
        "size": {"x": 32, "y": 32},
        "states": [{"name": name} for name in states],
    }
    existing = [state for state in sorted(os.listdir(path)) if state.endswith(".png")]
    meta["states"] = [{"name": state[:-4]} for state in existing]
    with open(os.path.join(path, "meta.json"), "w", encoding="utf-8") as file:
        json.dump(meta, file, indent=2)
        file.write("\n")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--font", default="/usr/share/fonts/truetype/wqy/wqy-zenhei.ttc")
    args = parser.parse_args()

    write_rsi(os.path.join(TEXTURES, "actions.rsi"),
              {name: medallion(glyph, PALETTE[colour], args.font) for name, (glyph, colour) in ACTIONS.items()})
    write_rsi(os.path.join(TEXTURES, "manuals.rsi"),
              {name: manual(glyph, PALETTE[colour], args.font)
               for name, (glyph, colour) in {**MANUALS, **ART_MANUALS}.items()})
    write_rsi(os.path.join(TEXTURES, "items.rsi"), {
        "talisman": talisman(args.font),
        "jade_slip": jade_slip(args.font),
        "blood_slip": blood_slip(args.font),
        "sect_plaque": sect_plaque(args.font),
        "incense": incense(lit=False),
        "incense_lit": incense(lit=True),
        **{kind: wuxia_weapon(kind) for kind in ("meteor_hammer", "rope_dart", "butterfly_sword", "iron_fan", "bamboo_staff",
                                                 "sect_jian", "needles", "flying_dagger", "smoke_pellet")},
        "qi_wall": qi_wall(),
    })
    for name, glyph, colour in (("body_exhaustion.rsi", "疲", "orange"), ("body_tempering.rsi", "锻", "gold"),
                                ("exploding_heart.rsi", "心", "crimson")):
        meter_path = os.path.join(TEXTURES, name)
        os.makedirs(meter_path, exist_ok=True)
        for digit in range(10):
            shutil.copy(os.path.join("Resources/Textures/Interface/Alerts/essence_counter.rsi", f"{digit}.png"),
                        os.path.join(meter_path, f"{digit}.png"))
        write_rsi(meter_path, {"base": meter_plate(glyph, colour, args.font)},
                  extra_copyright="; digits from essence_counter.rsi: Created by EmoGarbage404")
    write_rsi(os.path.join(TEXTURES, "cracks.rsi"), {f"crack{index}": floor_crack(seed=index * 7919) for index in range(1, 5)})
    write_rsi(os.path.join(TEXTURES, "internal_injury.rsi"),
              {f"injury{stacks}": injury_alert(stacks, args.font) for stacks in range(1, 4)})

    # The qi meter reuses the revenant essence counter's digits, so its attribution comes along.
    qi_path = os.path.join(TEXTURES, "qi_counter.rsi")
    os.makedirs(qi_path, exist_ok=True)
    essence = "Resources/Textures/Interface/Alerts/essence_counter.rsi"
    with open(os.path.join(essence, "meta.json"), encoding="utf-8") as file:
        essence_meta = json.load(file)
    for digit in range(10):
        shutil.copy(os.path.join(essence, f"{digit}.png"), os.path.join(qi_path, f"{digit}.png"))
    write_rsi(qi_path, {"qi": qi_alert()},
              extra_copyright="; digits from essence_counter.rsi: " + essence_meta.get("copyright", ""))


if __name__ == "__main__":
    main()
