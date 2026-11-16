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
    face, rim = colours
    size = 32
    image = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    pixels = image.load()
    centre = (size - 1) / 2
    outer, inner = 14.5, 12.0
    for y in range(size):
        for x in range(size):
            dx, dy = x - centre, y - centre
            distance = math.hypot(dx, dy)
            if distance > outer:
                continue
            # Light from the top left.
            light = max(0.0, min(1.0, 0.5 - (dx + dy) / (2 * outer)))
            if distance > inner:
                colour = lerp(rim, lerp(rim, face, 0.55), light)
            else:
                colour = lerp(lerp(face, rim, 0.35), face, light)
            pixels[x, y] = colour + (255,)

    mask = glyph_layer(glyph, font_path, 19, size)
    outline = mask.filter(ImageFilter.MaxFilter(3))
    dark = lerp(rim, (0, 0, 0), 0.5) + (255,)
    image.paste(Image.new("RGBA", (size, size), dark), (0, 0), outline)
    image.paste(Image.new("RGBA", (size, size), (252, 248, 232, 255)), (0, 0), mask)
    return image


def manual(glyph, colours, font_path):
    """A bound manual: a coloured cover with a pale title strip carrying the element's glyph."""
    face, rim = colours
    size = 32
    image = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    draw.rectangle((7, 4, 25, 28), fill=rim + (255,))
    draw.rectangle((8, 5, 24, 27), fill=face + (255,))
    # Stitched spine.
    draw.rectangle((7, 4, 9, 28), fill=lerp(rim, (0, 0, 0), 0.3) + (255,))
    for y in range(6, 28, 4):
        draw.point((8, y), fill=(236, 226, 196, 255))
    # Title strip.
    draw.rectangle((13, 7, 22, 25), fill=(236, 226, 196, 255))
    mask = glyph_layer(glyph, font_path, 10, size, threshold=60)
    shifted = Image.new("L", (size, size), 0)
    shifted.paste(mask, (2, 0))
    image.paste(Image.new("RGBA", (size, size), (40, 24, 16, 255)), (0, 0), shifted)
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


def array_circle(glyph, colour, font_path):
    """A floor circle of trigrams around a glyph."""
    face, rim = PALETTE[colour]
    image = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    draw.ellipse((1, 1, 30, 30), outline=face + (210,), width=1)
    draw.ellipse((5, 5, 26, 26), outline=rim + (180,), width=1)
    for angle in range(0, 360, 45):
        import math as _math
        x = 15.5 + 12.5 * _math.cos(_math.radians(angle))
        y = 15.5 + 12.5 * _math.sin(_math.radians(angle))
        draw.line((x - 1, y, x + 1, y), fill=face + (230,))
    mask = glyph_layer(glyph, font_path, 13, 32, threshold=70)
    image.paste(Image.new("RGBA", (32, 32), face + (200,)), (0, 0), mask)
    return image


def qi_wall():
    image = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    draw.rectangle((0, 0, 31, 31), fill=(250, 214, 112, 90), outline=(250, 214, 112, 200))
    for y in range(3, 32, 6):
        draw.line((2, y, 29, y), fill=(255, 246, 206, 120))
    return image


def mud():
    image = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    draw.ellipse((1, 3, 30, 29), fill=(92, 64, 36, 220))
    for x, y in ((9, 10), (19, 16), (12, 21), (22, 9)):
        draw.ellipse((x, y, x + 3, y + 2), fill=(134, 98, 58, 255))
    return image


def palm_shadow():
    image = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    draw.ellipse((0, 0, 31, 31), fill=(20, 16, 10, 110), outline=(250, 214, 112, 160))
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
        "array_gathering": array_circle("聚", "jade", args.font),
        "array_barrier": array_circle("阵", "gold", args.font),
        "array_alarm": array_circle("警", "orange", args.font),
        "qi_wall": qi_wall(),
        "mud": mud(),
        "palm_shadow": palm_shadow(),
    })
    for name, glyph, colour in (("body_exhaustion.rsi", "疲", "orange"), ("body_tempering.rsi", "锻", "gold")):
        meter_path = os.path.join(TEXTURES, name)
        os.makedirs(meter_path, exist_ok=True)
        for digit in range(10):
            shutil.copy(os.path.join("Resources/Textures/Interface/Alerts/essence_counter.rsi", f"{digit}.png"),
                        os.path.join(meter_path, f"{digit}.png"))
        write_rsi(meter_path, {"base": meter_plate(glyph, colour, args.font)},
                  extra_copyright="; digits from essence_counter.rsi: Created by EmoGarbage404")
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
