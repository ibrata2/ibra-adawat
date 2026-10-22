#!/usr/bin/env python3
"""Packs the Mac app icon into windows/assets/Adawat.ico (PNG entries, 16 to 256 px) and AppIcon-256.png.
Uses macOS `sips` for resizing. Run from the repository root: python3 windows/tools/make-ico.py"""
import pathlib, struct, subprocess, tempfile

root = pathlib.Path(__file__).resolve().parents[2]
source = root / "Resources" / "AppIcon-1024.png"
assets = root / "windows" / "assets"
assets.mkdir(exist_ok=True)
sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
images = []
with tempfile.TemporaryDirectory() as tmp:
    for size in sizes:
        out = pathlib.Path(tmp) / f"{size}.png"
        subprocess.run(["sips", "-z", str(size), str(size), str(source), "--out", str(out)], check=True, capture_output=True)
        images.append(out.read_bytes())
    (assets / "AppIcon-256.png").write_bytes(images[-1])
header = struct.pack("<HHH", 0, 1, len(images))
offset = 6 + 16 * len(images)
entries, data = b"", b""
for size, png in zip(sizes, images):
    entries += struct.pack("<BBBBHHII", size % 256, size % 256, 0, 0, 1, 32, len(png), offset + len(data))
    data += png
(assets / "Adawat.ico").write_bytes(header + entries + data)
print(assets / "Adawat.ico")
