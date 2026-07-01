#!/usr/bin/env python3
"""Generate the shipped green 'done' pointer asset (assets/done-cursor.cur).

Mirror of make-waiting-cursor.py: same 32x32 arrow with a white outline and a
top-left (0,0) hotspot, filled green instead of red. Green marks the "turn finished,
your move" (DONE) state; red marks WAITING. Re-run to regenerate the committed binary;
it has no runtime dependency.
"""
import struct
from pathlib import Path

W = H = 32

# Arrow shape, top-left aligned. 'X' = white outline, '.' = green fill, ' ' = clear.
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
GREEN = (0, 200, 0, 255)  # BGRA
WHITE = (255, 255, 255, 255)


def pixel(x, y):
    if y < len(ROWS) and x < len(ROWS[y]):
        c = ROWS[y][x]
        if c == "X":
            return WHITE
        if c == ".":
            return GREEN
    return CLEAR


# XOR (colour) data: 32bpp BGRA, bottom-up rows.
xor = bytearray()
for row in range(H - 1, -1, -1):
    for col in range(W):
        xor += bytes(pixel(col, row))

# AND (mask) data: 1bpp, rows padded to 4 bytes, bottom-up. Bit = 1 where the pixel
# is transparent, 0 where it is drawn. Deriving this from alpha (rather than leaving it
# all-zero) keeps the cursor correct on the GDI render path, which ignores the alpha
# channel and relies on this mask for transparency - otherwise the sprite paints as a
# solid black block.
and_mask = bytearray()
for row in range(H - 1, -1, -1):
    rowbytes = bytearray(4)  # 32 bits, padded to 4 bytes
    for col in range(W):
        if pixel(col, row)[3] == 0:           # transparent -> mask bit set
            rowbytes[col // 8] |= 0x80 >> (col % 8)
    and_mask += rowbytes

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

out = Path(__file__).resolve().parent.parent / "assets" / "done-cursor.cur"
out.parent.mkdir(parents=True, exist_ok=True)
out.write_bytes(icondir + entry + image)
print(f"wrote {out} ({out.stat().st_size} bytes)")
