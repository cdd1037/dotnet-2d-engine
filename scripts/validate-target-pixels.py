#!/usr/bin/env python3
"""Validate paired RGBA8 targets, translucent resolve and explicit pass behavior."""
from pathlib import Path
import sys
from PIL import Image

root = Path(sys.argv[1] if len(sys.argv) > 1 else "evidence/targets/jit")
names = ["composite", "retained", "failed-retained", "camera-clip", "clear",
         "new-transparent", "resized-target", "ui-window", "ui-target"]
images = {name: Image.open(root / (name + ".bmp")).convert("RGB") for name in names}
checks = 0


def check(value, why):
    global checks
    if not value:
        raise AssertionError(why)
    checks += 1


def pixel(name, x, y, rgb, tolerance=0):
    actual = images[name].getpixel((x, y))
    check(all(abs(a-b) <= tolerance for a, b in zip(actual, rgb)),
          f"{name} ({x},{y}): expected {rgb}, got {actual}")


check(all(image.size == (960, 540) for image in images.values()), "capture dimensions")
for name in ["retained", "failed-retained"]:
    check(images[name].tobytes() == images["composite"].tobytes(), f"{name} preserves prior target contents")
for x, expected in [(48,(128,0,0)), (80,(64,0,128)), (112,(0,0,128))]:
    pixel("composite", x, 48, expected, 2)
    pixel("composite", x, 176, expected, 1)
    top, direct = images["composite"].getpixel((x,48)), images["composite"].getpixel((x,176))
    check(all(abs(a-b) <= 2 for a,b in zip(top,direct)), "resolved translucent target matches direct compositing within RGBA8 rounding")
for x, expected in [(176,(32,0,0)), (208,(16,0,16)), (240,(0,0,16))]:
    pixel("composite", x, 48, expected, 2)
for x, expected in [(304,(27,27,27)), (336,(23,23,23)), (368,(9,9,9))]:
    pixel("composite", x, 48, expected, 2)
pixel("composite", 48, 104, (1,1,1))  # exactly one stored alpha quantum
for x in [48,176,304]:
    pixel("composite", x, 116, (0,0,0))  # alpha below one RGBA8 quantum resolves safely to zero
    pixel("composite", x, 124, (0,0,0))  # transparent clear
pixel("camera-clip", 64, 64, (0,255,0))
for x,y in [(55,64),(80,64),(64,47),(64,80)]:
    pixel("camera-clip", x,y,(0,0,0))
pixel("clear",48,48,(64,32,0),1)
check(images["new-transparent"].getbbox() is None, "new targets contain transparent black, not recycled texture data")
pixel("resized-target",32,32,(255,255,0));pixel("resized-target",79,63,(255,255,0))
pixel("resized-target",80,50,(0,0,0));pixel("resized-target",50,64,(0,0,0))
check(images["ui-window"].tobytes() == images["ui-target"].tobytes(), "UI renders only on the final window, without offscreen copies")
print(f"TARGET PIXELS PASS assertions={checks}; transparent RGBA8 resolve and two-pass tint")
