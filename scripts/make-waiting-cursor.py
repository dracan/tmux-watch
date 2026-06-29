#!/usr/bin/env python3
"""Generate the shipped red 'waiting' pointer asset (assets/waiting-cursor.cur).

A .cur file is an ICO-format container (type 2) holding one 32x32 32bpp image:
a red arrow with a white outline, hotspot at the top-left tip (0,0). Re-run this
to regenerate the committed binary; it has no runtime dependency.
"""
import struct
from pathlib import Path

W = H = 32

# Arrow shape, top-left aligned. 'X' = white outline, '.' = red fill, ' ' = clear.
ROWS = [
    "X",
    "XX",
    "X.X",
    "X..X",
    "X...X",
    "X....X",
    "X.....X",
    "X......X",
    "X.......X",
    "X........X",
    "X.........X",
    "X......XXXXX",
    "X...X..X",
    "X..XX..X",
    "X.X  X..X",
    "XX   X..X",
    "X     X..X",
    "      X..X",
    "       X..X",
    "       XXXX",
]

CLEAR = (0, 0, 0, 0)
RED = (0, 0, 255, 255)    # BGRA
WHITE = (255, 255, 255, 255)


def pixel(x, y):
    if y < len(ROWS) and x < len(ROWS[y]):
        c = ROWS[y][x]
        if c == "X":
            return WHITE
        if c == ".":
            return RED
    return CLEAR


# XOR (colour) data: 32bpp BGRA, bottom-up rows.
xor = bytearray()
for row in range(H - 1, -1, -1):
    for col in range(W):
        xor += bytes(pixel(col, row))

# AND (mask) data: 1bpp, rows padded to 4 bytes, bottom-up. All zero: alpha governs.
and_row = b"\x00" * 4
and_mask = and_row * H

# BITMAPINFOHEADER: height is doubled (XOR + AND), 32bpp.
bmih = struct.pack(
    "<IiiHHIIiiII",
    40, W, H * 2, 1, 32, 0, len(xor) + len(and_mask), 0, 0, 0, 0,
)
image = bmih + bytes(xor) + and_mask

# ICONDIR + one ICONDIRENTRY (cursor: hotspot replaces planes/bitcount fields).
icondir = struct.pack("<HHH", 0, 2, 1)
offset = 6 + 16
entry = struct.pack(
    "<BBBBHHII",
    W, H, 0, 0,        # width, height, colours, reserved
    0, 0,              # hotspot x, y (top-left tip)
    len(image), offset,
)

out = Path(__file__).resolve().parent.parent / "assets" / "waiting-cursor.cur"
out.parent.mkdir(parents=True, exist_ok=True)
out.write_bytes(icondir + entry + image)
print(f"wrote {out} ({out.stat().st_size} bytes)")
