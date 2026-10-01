#!/usr/bin/env python3
"""Check the five deterministic software-rendered movement captures."""
import sys
from pathlib import Path
from PIL import Image

root = Path(sys.argv[1])
checks = 0

def check(value, label):
    global checks
    if not value:
        raise AssertionError(label)
    checks += 1

for frame, center_x in [(45, 192), (90, 194), (225, 482), (400, 789), (599, 192)]:
    image = Image.open(root / f"frame-{frame:03}.bmp").convert("RGB")
    check(image.size == (960, 540), f"frame {frame}: viewport")
    check(image.getpixel((300, 400)) == (255, 0, 0), f"frame {frame}: floor")
    check(image.getpixel((815, 300)) == (0, 0, 255), f"frame {frame}: right wall")
    points = [(x, y) for y in range(160, 385) for x in range(96, 800)
              if (lambda rgb: rgb[0] > 220 and rgb[1] > 210 and 60 < rgb[2] < 140)(image.getpixel((x, y)))]
    check(bool(points), f"frame {frame}: player visible")
    xs, ys = zip(*points)
    check(abs((min(xs) + max(xs)) / 2 - center_x) <= 2, f"frame {frame}: player x")
    check(max(ys) < 350 if frame == 45 else max(ys) in (382, 383), f"frame {frame}: airborne/standing pose")

print(f"TILE MOVEMENT PIXELS PASS assertions={checks}")
