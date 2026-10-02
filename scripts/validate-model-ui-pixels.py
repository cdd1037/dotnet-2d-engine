#!/usr/bin/env python3
"""Check the actual 960x540 software-render readbacks of all three generic UIs.
These assertions establish visible content/structure, not physical GPU, real IME,
accessibility, or exact font rasterization across machines.
"""
import os
from pathlib import Path
from PIL import Image

root = Path(os.environ.get("GAL_MODEL_UI_CAPTURE_DIR", "evidence/ui-model/visual"))
checks = 0

def check(value, label):
    global checks
    assert value, label
    checks += 1

def near(image, xy, expected):
    actual = image.getpixel(xy)
    check(max(abs(a-b) for a, b in zip(actual, expected)) <= 3, (xy, actual, expected))

images = {name: Image.open(root / (name + ".bmp")).convert("RGB") for name in ("inventory", "dialogue", "settings")}
for name, image in images.items():
    check(image.size == (960, 540), name + " framebuffer dimensions")
    check(len(image.getcolors(image.width*image.height)) > 150, name + " contains rendered glyphs and controls")

inventory, dialogue, settings = (images[name] for name in ("inventory", "dialogue", "settings"))
near(inventory, (2, 2), (16, 25, 35))
near(inventory, (30, 35), (24, 38, 50))
near(inventory, (420, 145), (32, 60, 66))
near(inventory, (420, 260), (32, 51, 66))
blade = list(inventory.crop((58,149,130,221)).getdata())
lantern = list(inventory.crop((58,261,130,335)).getdata())
check(sum(b > 140 and g > 140 and r < g for r,g,b in blade) > 10, "manifested blade image pixels")
check(sum(r > 170 and g > 120 and b < 150 for r,g,b in lantern) > 30, "manifested lantern image pixels")
near(dialogue, (2, 2), (24, 22, 34))
near(dialogue, (55, 45), (39, 35, 53))
near(dialogue, (500, 135), (50, 44, 67))
near(settings, (2, 2), (32, 29, 24))
near(settings, (40, 40), (44, 41, 34))
near(settings, (78, 312), (229, 198, 138))
near(settings, (873, 210), (229, 198, 138))
near(settings, (873, 259), (32, 29, 24))
check(len({image.tobytes() for image in images.values()}) == 3, "three materially different authored layouts")
print(f"UI MODEL PIXELS PASS assertions={checks}; cards/images, dialogue, nested settings; software readback only")
