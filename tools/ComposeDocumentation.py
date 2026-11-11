"""Compose public preview assets from RuntimeChecks --render-docs output.

Requires Pillow. All panels come from the production C# renderer. No music or
reference-video frames are read. Usage: python tools/ComposeDocumentation.py INPUT OUTPUT
"""
import argparse
import shutil
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("input", type=Path)
parser.add_argument("output", type=Path)
args = parser.parse_args()
args.output.mkdir(parents=True, exist_ok=True)
font = ImageFont.truetype("C:/Windows/Fonts/segoeui.ttf", 29)
small = ImageFont.truetype("C:/Windows/Fonts/segoeui.ttf", 23)
rows = [
    ("media.png", "01   MEDIA CONTROLS  /  fixed F-page switch on the left"),
    ("function-keys.png", "02   FUNCTION KEYS  /  explicit page, no physical Fn state needed"),
    ("lyrics-aurora.png", "03   LYRICS + AURORA  /  center ribbons and side stardust"),
    ("lyrics-spectrum.png", "04   LYRICS + SPECTRUM  /  independent particle layers"),
    ("lyrics-off.png", "05   LYRICS ONLY  /  background off"),
]
sheet = Image.new("RGB", (2136, 840), (15, 18, 27))
draw = ImageDraw.Draw(sheet)
draw.text((64, 25), "TOUCH BAR FOR WINDOWS", font=font, fill=(234, 238, 255))
draw.text((64, 68), "Production views | 2008 x 60 panels | synthetic audio and sample text", font=small, fill=(147, 161, 187))
for index, (name, title) in enumerate(rows):
    y = 126 + index * 134
    draw.text((64, y), title, font=font, fill=(184, 198, 227))
    panel = Image.open(args.input / name).convert("RGB")
    sheet.paste(panel, (64, y + 43))
    shutil.copy2(args.input / name, args.output / name)
draw.text((64, 805), "Offscreen source rendering, not a hardware photograph. Tap lyrics: Aurora > Spectrum > Off.", font=small, fill=(147, 161, 187))
sheet.save(args.output / "overview.png")

for mode in ("aurora", "spectrum"):
    frames = [Image.open(path).convert("RGB") for path in sorted((args.input / (mode + "-frames")).glob("*.png"))]
    if not frames:
        raise RuntimeError("Missing rendered frames: " + mode)
    frames[0].save(args.output / (mode + "-demo.gif"), save_all=True, append_images=frames[1:],
                   duration=50, loop=0, disposal=2)
    if mode == "spectrum":
        # Right part of the center region; enlargement preserves panel geometry.
        details = [frame.crop((1028, 2, 1588, 58)).resize((1120, 112), Image.Resampling.LANCZOS) for frame in frames]
        details[0].save(args.output / "spectrum-detail.gif", save_all=True, append_images=details[1:],
                        duration=50, loop=0, disposal=2)
print("Composed documentation assets:", args.output)
