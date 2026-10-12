#!/usr/bin/env python3
"""Añade etiquetas a /workspace/texture-previews/contact-sheet.png (usa contact-sheet-labels.tsv)."""
from PIL import Image, ImageDraw, ImageFont
d = "/workspace/texture-previews/"
im = Image.open(d + "contact-sheet.png").convert("RGB")
dr = ImageDraw.Draw(im)
try:
    font = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf", 18)
except Exception:
    font = ImageFont.load_default()
for line in open(d + "contact-sheet-labels.tsv", encoding="utf-8"):
    parts = line.rstrip("\n").split("\t")
    if len(parts) != 3:
        continue
    text, x, y = parts[0], int(parts[1]), int(parts[2])
    w = dr.textlength(text, font=font)
    dr.rectangle([x + 6, y + 6, x + 14 + w, y + 32], fill=(0, 0, 0))
    dr.text((x + 10, y + 8), text, fill=(255, 235, 150), font=font)
im.save(d + "contact-sheet.png")
print("ok", im.size)
