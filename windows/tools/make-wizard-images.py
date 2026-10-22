#!/usr/bin/env python3
"""Draws the installer's side and corner pictures from the app icon, at 100%, 150% and 200% scale, into windows/installer/.
Needs Pillow. Run from the repository root: python3 windows/tools/make-wizard-images.py"""
import pathlib
from PIL import Image, ImageDraw

root = pathlib.Path(__file__).resolve().parents[2]
icon = Image.open(root / "Resources" / "AppIcon-1024.png").convert("RGBA")
out = root / "windows" / "installer"
top, bottom = (0x6B, 0x58, 0xF0), (0x2E, 0x22, 0x8C)

for scale in (100, 150, 200):
    # Side picture on the welcome and finish pages: a violet gradient with the icon.
    w, h = round(164 * scale / 100), round(314 * scale / 100)
    side = Image.new("RGB", (w, h))
    draw = ImageDraw.Draw(side)
    for y in range(h):
        t = y / (h - 1)
        draw.line([(0, y), (w, y)], fill=tuple(round(a + (b - a) * t) for a, b in zip(top, bottom)))
    size = round(w * 0.62)
    mark = icon.resize((size, size), Image.LANCZOS)
    side.paste(mark, ((w - size) // 2, round(h * 0.30)), mark)
    side.save(out / f"wizard-side-{scale}.bmp")

    # Corner picture on the inner pages: the icon on white.
    s = round(55 * scale / 100)
    corner = Image.new("RGB", (s, s), "white")
    mark = icon.resize((s, s), Image.LANCZOS)
    corner.paste(mark, (0, 0), mark)
    corner.save(out / f"wizard-corner-{scale}.bmp")
print("wrote", sorted(p.name for p in out.glob("wizard-*.bmp")))
