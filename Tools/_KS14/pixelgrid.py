# KS14: added in this fork
"""
Tiny helper for hand-placed pixel art: frames are written as rows of palette letters, so every pixel is chosen by
hand rather than painted by a formula. Symmetrical frames can be written as a left half and mirrored.
"""

from PIL import Image


def mirror_row(left):
    """A symmetrical row from its left half; the last character is the centre column."""
    return left + left[-2::-1]


def frame(rows, palette, top=0, left=0, size=32):
    """
    rows: list of strings, one per pixel row starting at `top`, each starting at column `left`.
    palette: letter -> RGB(A). '.' and ' ' are transparent.
    """
    image = Image.new("RGBA", (size, size))
    for row_index, row in enumerate(rows):
        for column_index, letter in enumerate(row):
            if letter in ". ":
                continue
            colour = palette[letter]
            if len(colour) == 3:
                colour = colour + (255,)
            x, y = left + column_index, top + row_index
            if 0 <= x < size and 0 <= y < size:
                image.putpixel((x, y), colour)
    return image


def flip(image):
    return image.transpose(Image.FLIP_LEFT_RIGHT)


def four_directions(south, north, east, west=None):
    """A 64x64 four-direction sheet in RSI order (south, north, east, west). West defaults to east mirrored."""
    sheet = Image.new("RGBA", (64, 64))
    sheet.paste(south, (0, 0))
    sheet.paste(north, (32, 0))
    sheet.paste(east, (0, 32))
    sheet.paste(west if west is not None else flip(east), (32, 32))
    return sheet
