#!/usr/bin/env python3
"""
Generates the original art for the "Escape from Klovn" lobby: the glitched wordmark, the hex emblem and
the navigation icons. Everything is drawn from primitives below; no third-party art is used.

    python Tools/_KS14/lobby_kharkiv_assets.py [--out DIR]

Output defaults to Resources/Textures/_KS14/Lobby/Kharkiv/.
"""
import argparse
import math
import os

import numpy as np
from PIL import Image, ImageChops, ImageDraw, ImageFilter

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
DEFAULT_OUT = os.path.join(REPO, "Resources", "Textures", "_KS14", "Lobby", "Kharkiv")

# ---------------------------------------------------------------- wordmark glyphs
# Each glyph is drawn on a 10 x 10 grid as polygons, so the letters can be stretched freely.


def R(x, y, w, h):
    return [(x, y), (x + w, y), (x + w, y + h), (x, y + h)]


GLYPHS = {
    "E": [R(0, 0, 3, 10), R(0, 0, 10, 3), R(0, 3.5, 8, 3), R(0, 7, 10, 3)],
    "S": [R(0, 0, 10, 3), R(0, 0, 3, 6.5), R(0, 3.5, 10, 3), R(7, 3.5, 3, 6.5), R(0, 7, 10, 3)],
    "C": [R(0, 0, 10, 3), R(0, 0, 3, 10), R(0, 7, 10, 3)],
    "A": [[(0, 3), (3, 0), (7, 0), (10, 3), (10, 10), (7, 10), (7, 6.5), (3, 6.5), (3, 10), (0, 10)],
          ],
    "P": [R(0, 0, 3, 10), R(0, 0, 10, 3), R(7, 0, 3, 6.5), R(0, 4, 10, 3)],
    "F": [R(0, 0, 3, 10), R(0, 0, 10, 3), R(0, 4, 8, 3)],
    "R": [R(0, 0, 3, 10), R(0, 0, 10, 3), R(7, 0, 3, 6.5), R(0, 4, 10, 3),
          [(4, 6.5), (7, 6.5), (10, 10), (7, 10)]],
    "O": [R(0, 0, 10, 3), R(0, 7, 10, 3), R(0, 0, 3, 10), R(7, 0, 3, 10)],
    "M": [R(0, 0, 3, 10), R(7, 0, 3, 10), [(3, 0), (5, 4.5), (7, 0), (7, 3.5), (5, 8), (3, 3.5)]],
    "K": [R(0, 0, 3, 10), [(3, 4), (7, 0), (10.5, 0), (6, 5), (10.5, 10), (7, 10), (3, 6)]],
    "L": [R(0, 0, 3, 10), R(0, 7, 10, 3)],
    "V": [[(0, 0), (3, 0), (5, 6.5), (7, 0), (10, 0), (6.5, 10), (3.5, 10)]],
    "N": [[(0, 0), (3, 0), (7, 6.5), (7, 0), (10, 0), (10, 10), (7, 10), (3, 3.5), (3, 10), (0, 10)]],
}


def render_line(text, width, height, gap_units, seed):
    """Draws a line of glyphs at 2x into an 'L' mask, stretching to exactly width x height."""
    ss = 2
    w, h = width * ss, height * ss
    count = len(text.replace(" ", ""))
    spaces = text.count(" ")
    total_units = count * 10 + (len(text) - 1 - spaces) * gap_units + spaces * (10 + gap_units)
    sx = w / (total_units + 0.0)
    sy = h / 10.0
    img = Image.new("L", (w, h), 0)
    draw = ImageDraw.Draw(img)
    x = 0.0
    for ch in text:
        if ch == " ":
            x += 10 + gap_units
            continue
        for poly in GLYPHS[ch]:
            draw.polygon([((x + px) * sx, py * sy) for px, py in poly], fill=255)
        x += 10 + gap_units
    return img, sx, sy


def stencil_cuts(mask, rng, rows, gap_px):
    """Slices thin horizontal slits through the letters, like a stencil's bridges."""
    draw = ImageDraw.Draw(mask)
    w, h = mask.size
    for frac in rows:
        y = int(h * frac)
        x = 0
        while x < w:
            seg = int(rng.integers(40, 220))
            if rng.random() < 0.62:
                draw.rectangle([x, y, x + seg, y + gap_px], fill=0)
            x += seg + int(rng.integers(20, 120))
    return mask


def glitch_bands(arr, rng, max_shift, bands, band_height):
    """Shifts random horizontal bands sideways, the way a bad signal tears a picture."""
    h, w = arr.shape[:2]
    out = arr.copy()
    for _ in range(bands):
        y = int(rng.integers(0, h))
        bh = int(rng.integers(band_height[0], band_height[1]))
        shift = int(rng.integers(-max_shift, max_shift + 1))
        out[y:y + bh] = np.roll(arr[y:y + bh], shift, axis=1)
    return out


def drips(alpha, rng, count, max_len, band):
    """Smears the bottom edge of the letters downward into thin vertical streaks."""
    h, w = alpha.shape
    out = alpha.astype(np.float32)
    ys = np.where(alpha.max(axis=1) > 0)[0]
    if len(ys) == 0:
        return alpha
    for _ in range(count):
        x = int(rng.integers(0, w))
        col = alpha[:, x]
        filled = np.where(col > 128)[0]
        if len(filled) == 0:
            continue
        bottom = int(filled.max())
        if bottom + 2 >= h:
            continue
        length = int(rng.integers(8, max_len))
        width = int(rng.integers(1, 5))
        end = min(h, bottom + length)
        fade = np.linspace(1.0, 0.0, end - bottom)
        for i, f in enumerate(fade):
            out[bottom + i, x:x + width] = np.maximum(out[bottom + i, x:x + width], 255 * f * rng.uniform(0.5, 1.0))
    return np.clip(out, 0, 255).astype(np.uint8)


def erode(alpha, rng, strength, scale):
    """Eats away at the letters with blocky noise, heaviest toward the bottom."""
    h, w = alpha.shape
    small = rng.random((h // scale + 1, w // scale + 1)).astype(np.float32)
    noise = np.kron(small, np.ones((scale, scale), dtype=np.float32))[:h, :w]
    grad = np.linspace(0.0, 1.0, h, dtype=np.float32)[:, None]
    threshold = strength * (0.15 + 0.85 * grad ** 1.6)
    keep = noise > threshold
    return (alpha * keep).astype(np.uint8)


def colourise(alpha, top, mid, bottom, rng):
    h, w = alpha.shape
    t = np.linspace(0.0, 1.0, h, dtype=np.float32)[:, None]
    top, mid, bottom = (np.array(c, dtype=np.float32) for c in (top, mid, bottom))
    upper = (t < 0.55)
    k = np.where(upper, t / 0.55, (t - 0.55) / 0.45)[..., None]
    rgb = np.where(upper[..., None], top * (1 - k) + mid * k, mid * (1 - k) + bottom * k)
    rgb = np.broadcast_to(rgb, (h, w, 3)).copy()
    # scanlines and grain
    scan = (np.arange(h) % 4 < 1).astype(np.float32)[:, None, None]
    rgb *= 1.0 - 0.22 * scan
    rgb *= 1.0 + rng.normal(0, 0.05, (h, w, 1)).astype(np.float32)
    return np.clip(rgb, 0, 255)


def build_line(text, width, height, gap, seed, cuts, erosion, shift, drip_count, drip_len):
    rng = np.random.default_rng(seed)
    mask, _, _ = render_line(text, width, height, gap, seed)
    mask = stencil_cuts(mask, rng, cuts, max(3, height // 60))
    big = np.array(mask)
    big = glitch_bands(big, rng, shift, 14, (6, 34))
    big = erode(big, rng, erosion, 3)
    big = drips(big, rng, drip_count, drip_len, 0)
    ss_img = Image.fromarray(big, "L").resize((width, height), Image.LANCZOS)
    alpha = np.array(ss_img)
    rgb = colourise(alpha, (245, 249, 252), (150, 178, 198), (70, 98, 122), rng)
    return alpha, rgb


def compose_wordmark(out_path):
    W, H = 2400, 980
    pad_x = 60
    inner = W - pad_x * 2
    # (text, height, gap units, y, seed)
    top_alpha, top_rgb = build_line("ESCAPE FROM", inner, 200, 2.4, 11, [], 0.05, 16, 40, 30)
    big_alpha, big_rgb = build_line("KLOVN", inner, 520, 1.4, 23, [0.5], 0.09, 46, 130, 150)

    canvas_rgb = np.zeros((H, W, 3), dtype=np.float32)
    canvas_a = np.zeros((H, W), dtype=np.float32)

    def paste(alpha, rgb, y):
        h = alpha.shape[0]
        canvas_a[y:y + h, pad_x:pad_x + alpha.shape[1]] = np.maximum(
            canvas_a[y:y + h, pad_x:pad_x + alpha.shape[1]], alpha)
        region = canvas_rgb[y:y + h, pad_x:pad_x + alpha.shape[1]]
        mix = (alpha.astype(np.float32) / 255.0)[..., None]
        canvas_rgb[y:y + h, pad_x:pad_x + alpha.shape[1]] = region * (1 - mix) + rgb * mix

    paste(top_alpha, top_rgb, 70)
    paste(big_alpha, big_rgb, 70 + 200 + 60)

    # chromatic fringe: red leans left, blue right, only where the letter has an edge
    a = canvas_a.astype(np.uint8)
    red = np.roll(a, -6, axis=1)
    blue = np.roll(a, 6, axis=1)
    fringe_r = np.clip(red.astype(np.int16) - a.astype(np.int16), 0, 255).astype(np.float32) / 255.0
    fringe_b = np.clip(blue.astype(np.int16) - a.astype(np.int16), 0, 255).astype(np.float32) / 255.0
    rgba = np.zeros((H, W, 4), dtype=np.float32)
    rgba[..., :3] = canvas_rgb
    rgba[..., 3] = canvas_a

    out_a = rgba[..., 3] / 255.0
    out_rgb = canvas_rgb * out_a[..., None]
    out_rgb[..., 0] += 255 * 0.3 * fringe_r
    out_rgb[..., 1] += 40 * fringe_r
    out_rgb[..., 2] += 255 * 0.35 * fringe_b
    out_rgb[..., 1] += 160 * 0.25 * fringe_b
    out_a = np.clip(out_a + fringe_r * 0.5 + fringe_b * 0.5, 0, 1)

    # soft blue bloom behind everything
    bloom = Image.fromarray((np.clip(canvas_a, 0, 255)).astype(np.uint8), "L").filter(ImageFilter.GaussianBlur(26))
    bloom_a = np.array(bloom).astype(np.float32) / 255.0 * 0.38
    final_a = np.clip(np.maximum(out_a, bloom_a), 0, 1)
    final_rgb = np.where(final_a[..., None] > 0,
                         (out_rgb + np.array([60, 110, 150], dtype=np.float32) * bloom_a[..., None] * (1 - out_a[..., None]))
                         / np.maximum(final_a[..., None], 1e-4), 0)
    final = np.dstack([np.clip(final_rgb, 0, 255), final_a * 255]).astype(np.uint8)
    Image.fromarray(final, "RGBA").save(out_path)


# ---------------------------------------------------------------- icons

SS = 8  # supersampling


def canvas(size):
    return Image.new("RGBA", (size * SS, size * SS), (0, 0, 0, 0))


def finish(img, size):
    return img.resize((size, size), Image.LANCZOS)


WHITE = (255, 255, 255, 255)


def ico_menu(d, s):
    for i in range(3):
        y = s * (0.26 + 0.24 * i)
        d.rounded_rectangle([s * 0.16, y, s * 0.84, y + s * 0.09], radius=s * 0.02, fill=WHITE)


def hexagon(cx, cy, r, rot=0.0):
    return [(cx + r * math.cos(math.radians(60 * i + rot)), cy + r * math.sin(math.radians(60 * i + rot)))
            for i in range(6)]


def ico_hideout(d, s):
    cx, cy = s / 2, s / 2
    d.polygon(hexagon(cx, cy, s * 0.4, 30), fill=WHITE)
    d.polygon(hexagon(cx, cy, s * 0.27, 30), fill=(0, 0, 0, 0))
    # a cube inside
    d.polygon([(cx, cy - s * 0.2), (cx + s * 0.17, cy - s * 0.1), (cx, cy), (cx - s * 0.17, cy - s * 0.1)], fill=WHITE)
    d.polygon([(cx - s * 0.17, cy - s * 0.04), (cx, cy + s * 0.06), (cx, cy + s * 0.22), (cx - s * 0.17, cy + s * 0.12)], fill=WHITE)
    d.polygon([(cx + s * 0.17, cy - s * 0.04), (cx, cy + s * 0.06), (cx, cy + s * 0.22), (cx + s * 0.17, cy + s * 0.12)], fill=WHITE)


def ico_player(d, s):
    d.ellipse([s * 0.32, s * 0.1, s * 0.68, s * 0.46], fill=WHITE)
    d.pieslice([s * 0.12, s * 0.5, s * 0.88, s * 1.3], 180, 360, fill=WHITE)


def ico_character(d, s):
    # person with a rifle-sling stripe, the way the original shows a soldier
    ico_player(d, s)
    d.polygon([(s * 0.2, s * 0.9), (s * 0.5, s * 0.58), (s * 0.58, s * 0.66), (s * 0.3, s * 0.96)], fill=(0, 0, 0, 0))


def ico_cart(d, s):
    d.polygon([(s * 0.06, s * 0.16), (s * 0.24, s * 0.16), (s * 0.3, s * 0.32), (s * 0.9, s * 0.32),
               (s * 0.8, s * 0.64), (s * 0.34, s * 0.64)], fill=WHITE)
    d.line([(s * 0.2, s * 0.16), (s * 0.36, s * 0.74), (s * 0.78, s * 0.74)], fill=WHITE, width=int(s * 0.07))
    d.ellipse([s * 0.3, s * 0.78, s * 0.44, s * 0.92], fill=WHITE)
    d.ellipse([s * 0.64, s * 0.78, s * 0.78, s * 0.92], fill=WHITE)


def arrow(d, pts, head, width):
    d.line(pts, fill=WHITE, width=width, joint="curve")
    d.polygon(head, fill=WHITE)


def ico_flea(d, s):
    w = int(s * 0.1)
    d.arc([s * 0.14, s * 0.14, s * 0.86, s * 0.86], 200, 330, fill=WHITE, width=w)
    d.polygon([(s * 0.78, s * 0.14), (s * 0.9, s * 0.38), (s * 0.58, s * 0.34)], fill=WHITE)
    d.arc([s * 0.14, s * 0.14, s * 0.86, s * 0.86], 20, 150, fill=WHITE, width=w)
    d.polygon([(s * 0.22, s * 0.86), (s * 0.1, s * 0.62), (s * 0.42, s * 0.66)], fill=WHITE)


def ico_builds(d, s):
    # a stack of parts
    d.rounded_rectangle([s * 0.12, s * 0.52, s * 0.88, s * 0.78], radius=s * 0.04, fill=WHITE)
    d.rounded_rectangle([s * 0.26, s * 0.24, s * 0.74, s * 0.46], radius=s * 0.04, fill=WHITE)
    d.rectangle([s * 0.38, s * 0.1, s * 0.62, s * 0.24], fill=WHITE)
    d.rectangle([s * 0.22, s * 0.6, s * 0.3, s * 0.7], fill=(0, 0, 0, 0))
    d.rectangle([s * 0.7, s * 0.6, s * 0.78, s * 0.7], fill=(0, 0, 0, 0))


def ico_handbook(d, s):
    d.rounded_rectangle([s * 0.18, s * 0.08, s * 0.82, s * 0.92], radius=s * 0.06, fill=WHITE)
    d.rectangle([s * 0.44, s * 0.2, s * 0.56, s * 0.3], fill=(0, 0, 0, 0))
    d.rectangle([s * 0.44, s * 0.38, s * 0.56, s * 0.78], fill=(0, 0, 0, 0))


def ico_messenger(d, s):
    d.rounded_rectangle([s * 0.08, s * 0.16, s * 0.92, s * 0.7], radius=s * 0.06, fill=WHITE)
    d.polygon([(s * 0.24, s * 0.66), (s * 0.24, s * 0.9), (s * 0.5, s * 0.68)], fill=WHITE)
    for i in range(3):
        y = s * (0.28 + 0.12 * i)
        d.rectangle([s * 0.2, y, s * (0.8 - 0.1 * i), y + s * 0.05], fill=(0, 0, 0, 0))


def ico_survey(d, s):
    pts = []
    for i in range(10):
        r = s * (0.44 if i % 2 == 0 else 0.2)
        a = math.radians(-90 + 36 * i)
        pts.append((s / 2 + r * math.cos(a), s / 2 + r * math.sin(a)))
    d.polygon(pts, fill=WHITE)


def ico_expansions(d, s):
    # crouching operator, blocky
    d.ellipse([s * 0.4, s * 0.06, s * 0.62, s * 0.28], fill=WHITE)
    d.polygon([(s * 0.3, s * 0.32), (s * 0.66, s * 0.3), (s * 0.78, s * 0.56), (s * 0.62, s * 0.6),
               (s * 0.6, s * 0.94), (s * 0.4, s * 0.94), (s * 0.36, s * 0.64), (s * 0.22, s * 0.62)], fill=WHITE)
    d.rectangle([s * 0.62, s * 0.4, s * 0.96, s * 0.5], fill=WHITE)


def ico_cog(d, s):
    cx, cy = s / 2, s / 2
    pts = []
    teeth = 8
    for i in range(teeth * 2):
        r = s * (0.44 if i % 2 == 0 else 0.33)
        a0 = math.radians(360 / (teeth * 2) * i - 360 / (teeth * 4))
        a1 = math.radians(360 / (teeth * 2) * (i + 1) - 360 / (teeth * 4))
        pts += [(cx + r * math.cos(a0), cy + r * math.sin(a0)), (cx + r * math.cos(a1), cy + r * math.sin(a1))]
    d.polygon(pts, fill=WHITE)
    d.ellipse([cx - s * 0.14, cy - s * 0.14, cx + s * 0.14, cy + s * 0.14], fill=(0, 0, 0, 0))


def ico_plus(d, s):
    d.rectangle([s * 0.46, s * 0.14, s * 0.54, s * 0.86], fill=WHITE)
    d.rectangle([s * 0.14, s * 0.46, s * 0.86, s * 0.54], fill=WHITE)


def ico_warning(d, s):
    d.polygon([(s * 0.5, s * 0.08), (s * 0.94, s * 0.88), (s * 0.06, s * 0.88)], fill=WHITE)
    d.rectangle([s * 0.46, s * 0.34, s * 0.54, s * 0.62], fill=(0, 0, 0, 0))
    d.ellipse([s * 0.45, s * 0.68, s * 0.55, s * 0.78], fill=(0, 0, 0, 0))


def ico_exclaim(d, s):
    d.rectangle([s * 0.42, s * 0.12, s * 0.58, s * 0.62], fill=WHITE)
    d.rectangle([s * 0.42, s * 0.72, s * 0.58, s * 0.88], fill=WHITE)


def ico_record(d, s):
    d.ellipse([s * 0.1, s * 0.1, s * 0.9, s * 0.9], fill=WHITE)
    d.polygon([(s * 0.42, s * 0.3), (s * 0.7, s * 0.5), (s * 0.42, s * 0.7)], fill=(0, 0, 0, 0))


def ico_coin(d, s):
    # a diamond token with a cut-out centre mark
    d.polygon([(s * 0.5, s * 0.04), (s * 0.96, s * 0.5), (s * 0.5, s * 0.96), (s * 0.04, s * 0.5)], fill=WHITE)
    d.polygon([(s * 0.5, s * 0.22), (s * 0.78, s * 0.5), (s * 0.5, s * 0.78), (s * 0.22, s * 0.5)], fill=(0, 0, 0, 0))
    d.rectangle([s * 0.44, s * 0.36, s * 0.56, s * 0.64], fill=WHITE)


ICONS = {
    "menu": ico_menu, "hideout": ico_hideout, "player": ico_player, "character": ico_character, "cart": ico_cart,
    "flea": ico_flea, "builds": ico_builds, "handbook": ico_handbook, "messenger": ico_messenger,
    "survey": ico_survey, "expansions": ico_expansions, "cog": ico_cog, "plus": ico_plus,
    "warning": ico_warning, "exclaim": ico_exclaim, "record": ico_record, "coin": ico_coin,
}


def make_icon(name, size=48):
    img = canvas(size)
    d = ImageDraw.Draw(img)
    ICONS[name](d, size * SS)
    # knock-outs drew transparent pixels over white: composite properly by redrawing on a mask
    return finish(img, size)


# ---------------------------------------------------------------- emblems


def clown_skull(d, s, ink, cut):
    """A skull with a jester's collar: the lobby's hex badge."""
    cx = s / 2
    d.ellipse([cx - s * 0.24, s * 0.2, cx + s * 0.24, s * 0.64], fill=ink)
    d.rounded_rectangle([cx - s * 0.14, s * 0.56, cx + s * 0.14, s * 0.78], radius=s * 0.03, fill=ink)
    d.ellipse([cx - s * 0.17, s * 0.36, cx - s * 0.04, s * 0.5], fill=cut)
    d.ellipse([cx + s * 0.04, s * 0.36, cx + s * 0.17, s * 0.5], fill=cut)
    d.polygon([(cx, s * 0.5), (cx - s * 0.04, s * 0.58), (cx + s * 0.04, s * 0.58)], fill=cut)
    for i in range(4):
        x = cx - s * 0.1 + i * s * 0.067
        d.rectangle([x, s * 0.68, x + s * 0.02, s * 0.78], fill=cut)
    # jester hat points
    d.polygon([(cx - s * 0.24, s * 0.3), (cx - s * 0.38, s * 0.1), (cx - s * 0.1, s * 0.2)], fill=ink)
    d.polygon([(cx + s * 0.24, s * 0.3), (cx + s * 0.38, s * 0.1), (cx + s * 0.1, s * 0.2)], fill=ink)
    d.ellipse([cx - s * 0.43, s * 0.05, cx - s * 0.33, s * 0.15], fill=ink)
    d.ellipse([cx + s * 0.33, s * 0.05, cx + s * 0.43, s * 0.15], fill=ink)


def make_emblem(size, ring_colour, fill_colour, ink):
    img = canvas(size)
    d = ImageDraw.Draw(img)
    s = size * SS
    d.polygon(hexagon(s / 2, s / 2, s * 0.49, 30), fill=ring_colour)
    d.polygon(hexagon(s / 2, s / 2, s * 0.43, 30), fill=fill_colour)
    d.polygon(hexagon(s / 2, s / 2, s * 0.39, 30), outline=(186, 70, 46, 255), width=int(s * 0.03))
    clown_skull(d, s, ink, fill_colour)
    return finish(img, size)


def make_pve_emblem(size):
    img = canvas(size)
    d = ImageDraw.Draw(img)
    s = size * SS
    d.ellipse([s * 0.03, s * 0.03, s * 0.97, s * 0.97], outline=(214, 214, 214, 255), width=int(s * 0.05))
    d.ellipse([s * 0.14, s * 0.14, s * 0.86, s * 0.86], fill=(214, 214, 214, 255))
    clown_skull(d, s, (28, 30, 30, 255), (214, 214, 214, 255))
    return finish(img, size)


# ---------------------------------------------------------------- season banner

# A blocky 5 x 7 pixel font, drawn bold (each pixel a square cell), for the banner's lockup.
PIXEL_FONT = {
    "H": ["10001", "10001", "10001", "11111", "10001", "10001", "10001"],
    "O": ["01110", "10001", "10001", "10001", "10001", "10001", "01110"],
    "N": ["10001", "11001", "10101", "10011", "10001", "10001", "10001"],
    "K": ["10001", "10010", "10100", "11000", "10100", "10010", "10001"],
    "P": ["11110", "10001", "10001", "11110", "10000", "10000", "10000"],
    "R": ["11110", "10001", "10001", "11110", "10100", "10010", "10001"],
    "T": ["11111", "00100", "00100", "00100", "00100", "00100", "00100"],
    "C": ["01111", "10000", "10000", "10000", "10000", "10000", "01111"],
    "L": ["10000", "10000", "10000", "10000", "10000", "10000", "11111"],
    "S": ["01111", "10000", "10000", "01110", "00001", "00001", "11110"],
    "E": ["11111", "10000", "10000", "11110", "10000", "10000", "11111"],
    "A": ["01110", "10001", "10001", "11111", "10001", "10001", "10001"],
    "1": ["00100", "01100", "00100", "00100", "00100", "00100", "01110"],
    " ": ["00000", "00000", "00000", "00000", "00000", "00000", "00000"],
}


def pixel_text_width(text, cell, gap):
    return len(text) * 5 * cell + (len(text) - 1) * gap


def draw_pixel_text(draw, x, y, text, cell, gap, colour, rng=None, jitter=0.0):
    for ch in text:
        rows = PIXEL_FONT[ch]
        for ry, row in enumerate(rows):
            for rx, bit in enumerate(row):
                if bit != "1":
                    continue
                c = colour
                if rng is not None and jitter > 0:
                    k = 1.0 - rng.random() * jitter
                    c = tuple(int(v * k) for v in colour[:3]) + (colour[3],)
                px, py = x + rx * cell, y + ry * cell
                draw.rectangle([px, py, px + cell + 1, py + cell - 1], fill=c)  # a little wider than tall: bold strokes
        x += 5 * cell + gap
    return x


def make_lockup(path):
    """HONK on a grainy white plate under a glowing pixel skyline, then PROTOCOL spaced out in white."""
    rng = np.random.default_rng(5)
    cell = 4
    H = 96
    plate_top, plate_h = 40, 44
    main_gap, sub_gap = 9, 13
    main_w = pixel_text_width("HONK", cell, main_gap)
    sub_w = pixel_text_width("PROTOCOL", cell, sub_gap)
    plate_w = main_w + 30
    W = plate_w + 18 + sub_w + 14
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))

    # Skyline: a circus big top among station towers, mint, dithered, standing on the plate.
    sky = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    sd = ImageDraw.Draw(sky)
    base = plate_top
    mint = (150, 236, 206, 255)
    shapes = [
        [(18, base), (18, base - 10), (24, base - 10), (24, base)],
        [(28, base), (28, base - 18), (31, base - 22), (34, base - 18), (34, base)],
        [(38, base), (38, base - 13), (46, base - 13), (46, base)],
        [(52, base), (56, base - 14), (72, base - 30), (73, base - 37), (74, base - 30), (90, base - 14), (94, base)],
        [(98, base), (98, base - 24), (101, base - 24), (101, base - 31), (104, base - 31), (104, base - 24),
         (107, base - 24), (107, base)],
        [(110, base), (110, base - 15), (120, base - 15), (120, base)],
        [(123, base), (123, base - 9), (130, base - 9), (130, base)],
    ]
    for poly in shapes:
        sd.polygon(poly, fill=mint)
    arr = np.array(sky)
    # Dither: a checker of pixels plus random ones, solid near the base.
    yy, xx = np.mgrid[0:H, 0:W]
    keep = ((xx + yy) % 2 == 0) | (rng.random((H, W)) < 0.35) | (yy > base - 6)
    arr[..., 3] = np.where(keep, arr[..., 3], 0)
    sky = Image.fromarray(arr, "RGBA")
    glow = np.array(sky.filter(ImageFilter.GaussianBlur(4))).astype(np.float32)
    glow[..., 3] *= 0.9
    img.alpha_composite(Image.fromarray(glow.astype(np.uint8), "RGBA"))
    img.alpha_composite(sky)

    # The plate: off-white, speckled.
    plate = np.zeros((plate_h, plate_w, 4), dtype=np.float32)
    plate[..., :3] = np.array([236, 240, 238], dtype=np.float32)
    plate[..., :3] *= (1.0 - rng.random((plate_h, plate_w, 1)) ** 6 * 0.55)
    plate[..., 3] = 255
    img.alpha_composite(Image.fromarray(plate.astype(np.uint8), "RGBA"), (0, plate_top))
    d = ImageDraw.Draw(img)
    text_y = plate_top + (plate_h - 7 * cell) // 2
    draw_pixel_text(d, 15, text_y, "HONK", cell, main_gap, (22, 30, 36, 255), rng, 0.35)

    # PROTOCOL, white, with a little grain, and a tick after it.
    sub_x = plate_w + 18
    draw_pixel_text(d, sub_x, text_y, "PROTOCOL", cell, sub_gap, (240, 246, 244, 255), rng, 0.25)
    season = "SEASON 1"
    season_cell, season_gap = 2, 6
    season_w = pixel_text_width(season, season_cell, season_gap)
    season_x = sub_x + sub_w - season_w
    season_y = plate_top - 7 * season_cell - 6
    season_colour = (128, 214, 186, 255)
    d.rectangle([season_x - 16, season_y + 2, season_x - 7, season_y + 11], fill=season_colour)
    draw_pixel_text(d, season_x, season_y, season, season_cell, season_gap, season_colour)
    tx, ty = sub_x + sub_w + 4, plate_top + plate_h - 2
    d.polygon([(tx, ty), (tx + 6, ty), (tx + 6, ty - 6)], fill=(170, 230, 210, 255))
    img.save(path)


BANNER_SIZE = (820, 120)
BANNER_PAD = 14  # transparent margin round the box for the outer glow; the stylebox expands by this much


def make_banner_panel(path):
    """The season banner's box at its real size: dark green-teal glass with grain and a lighter bloom behind the lockup,
    an inner glow along the edges (strongest at the top), a thin mint border with bright L-shaped brackets on the
    corners, and a soft green halo spilling outside it into a transparent margin."""
    rng = np.random.default_rng(9)
    w, h = BANNER_SIZE
    pad = BANNER_PAD
    W, H = w + pad * 2, h + pad * 2

    # Outer halo: the box's outline, blurred, mint.
    halo = Image.new("L", (W, H), 0)
    ImageDraw.Draw(halo).rectangle([pad, pad, pad + w - 1, pad + h - 1], outline=255, width=3)
    halo = np.array(halo.filter(ImageFilter.GaussianBlur(6))).astype(np.float32) / 255.0
    out = np.zeros((H, W, 4), dtype=np.float32)
    out[..., :3] = np.array([110, 200, 170], dtype=np.float32)
    out[..., 3] = halo * 0.55

    # Body.
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    t = yy / (h - 1)
    top = np.array([36, 68, 61], dtype=np.float32)
    bottom = np.array([17, 36, 33], dtype=np.float32)
    rgb = top * (1 - t[..., None]) + bottom * t[..., None]
    # The glass's own texture, under every glow: a grid of small cells in a plaid, each row and each column a slightly
    #     different shade, with a little variation per cell on top.
    cell = 3
    rows = int(np.ceil(h / cell))
    cols = int(np.ceil(w / cell))
    row_shade = rng.normal(0, 0.055, rows).astype(np.float32)
    col_shade = rng.normal(0, 0.055, cols).astype(np.float32)
    cell_shade = rng.normal(0, 0.035, (rows, cols)).astype(np.float32)
    grid = 1.0 + row_shade[:, None] + col_shade[None, :] + cell_shade
    grid = np.kron(grid, np.ones((cell, cell), dtype=np.float32))[:h, :w]
    rgb *= grid[..., None]

    # The glows go over the grid by blending toward lit glass, not by adding, so under them the grid washes out.
    lit = np.array([92, 150, 132], dtype=np.float32)

    def glow_over(amount):
        rgb[...] = rgb * (1.0 - amount[..., None]) + lit * amount[..., None]

    # Broad bloom behind the lockup (left third), as if the plate lit the glass.
    bx, by = w * 0.3, h * 0.55
    glow_over(0.32 * np.exp(-(((xx - bx) / (w * 0.32)) ** 2 + ((yy - by) / (h * 0.75)) ** 2)))

    # Tighter glow round the things that shine: the white plate and the emblem (where the banner lays them out).
    def rect_glow(x0, y0, x1, y1, radius, strength):
        dx = np.maximum(np.maximum(x0 - xx, xx - x1), 0.0)
        dy = np.maximum(np.maximum(y0 - yy, yy - y1), 0.0)
        return strength * np.exp(-np.sqrt(dx * dx + dy * dy) / radius)

    glow_over(rect_glow(104, 52, 240, 94, 16.0, 0.5))      # the HONK plate
    glow_over(rect_glow(140, 20, 236, 52, 12.0, 0.25))     # the skyline over it
    glow_over(rect_glow(26, 32, 82, 88, 14.0, 0.3))        # the emblem

    # Inner glow along the edges: distance to the nearest edge, the top edge counting double.
    edge = np.minimum(np.minimum(xx, w - 1 - xx), np.minimum(yy * 0.6, h - 1 - yy))
    glow_over(0.55 * np.exp(-edge / 5.0))

    # Fine grain over everything, and faint vertical streaks.
    rgb *= 1.0 + rng.normal(0, 0.03, (h, w, 1)).astype(np.float32)
    rgb *= 1.0 + (rng.random((1, w, 1)).astype(np.float32) - 0.5) * 0.04
    # Alpha is 0..1 throughout: the body is nearly opaque glass over the halo.
    out[pad:pad + h, pad:pad + w, :3] = np.clip(rgb, 0, 255)
    out[pad:pad + h, pad:pad + w, 3] = 0.94
    img = Image.fromarray(np.dstack([out[..., :3], out[..., 3:4] * 255.0]).astype(np.uint8), "RGBA")
    d = ImageDraw.Draw(img)
    x0, y0, x1, y1 = pad, pad, pad + w - 1, pad + h - 1
    d.rectangle([x0, y0, x1, y1], outline=(104, 160, 140, 235))
    d.rectangle([x0 + 1, y0 + 1, x1 - 1, y1 - 1], outline=(52, 92, 82, 200))
    # Corner brackets: bright, two pixels thick, sitting on the border.
    bright = (214, 244, 234, 255)
    length = 9
    for (cx, cy, sx, sy) in [(x0, y0, 1, 1), (x1, y0, -1, 1), (x0, y1, 1, -1), (x1, y1, -1, -1)]:
        for k in range(2):
            d.line([(cx, cy + sy * k), (cx + sx * length, cy + sy * k)], fill=bright)
            d.line([(cx + sx * k, cy), (cx + sx * k, cy + sy * length)], fill=bright)
    img.save(path)

def save(img, out, name):
    img.save(os.path.join(out, name))
    print("wrote", name)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--out", default=DEFAULT_OUT)
    args = parser.parse_args()
    os.makedirs(args.out, exist_ok=True)

    compose_wordmark(os.path.join(args.out, "wordmark.png"))
    print("wrote wordmark.png")

    for name in ICONS:
        save(make_icon(name, 64), args.out, f"icon_{name}.png")

    save(make_emblem(256, (110, 150, 142, 255), (24, 36, 38, 255), (222, 232, 230, 255)), args.out, "emblem_hex.png")
    save(make_pve_emblem(128), args.out, "emblem_pve.png")
    make_lockup(os.path.join(args.out, "lockup.png"))
    print("wrote lockup.png")
    make_banner_panel(os.path.join(args.out, "banner_panel.png"))
    print("wrote banner_panel.png")


if __name__ == "__main__":
    main()
