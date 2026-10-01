#!/usr/bin/env python3
"""Check material parameters/order/default restoration from actual readbacks."""
from pathlib import Path
import sys
from PIL import Image

root = Path(sys.argv[1] if len(sys.argv) > 1 else "evidence/materials/jit")
images = {name: Image.open(root / (name + ".bmp")).convert("RGB") for name in
          ["materials", "clipped", "copied", "legacy-reset", "reloaded"]}
count = 0


def check(condition, why):
    global count
    if not condition:
        raise AssertionError(why)
    count += 1


def pixel(name, point, expected, tolerance=0):
    actual = images[name].getpixel(point)
    check(all(abs(a-b) <= tolerance for a, b in zip(actual, expected)),
          f"{name} {point}: expected {expected}, got {actual}")


clear = (9, 11, 20)
check(all(image.size == (960, 540) for image in images.values()), "fixture dimensions")
for name in ["materials", "clipped"]:
    pixel(name, (64, 64), (64, 89, 178))
    pixel(name, (176, 64), (255, 0, 0))
    pixel(name, (288, 64), (0, 255, 0))
    pixel(name, (400, 64), (54, 54, 54))
    pixel(name, (512, 64), (0, 128, 255))
    # Red half-alpha then blue half-alpha, over the clear color; permit UNORM rounding.
    pixel(name, (64, 192), (66, 3, 133), 1)
    pixel(name, (176, 192), (255, 255, 0))
    pixel(name, (288, 192), (255, 255, 0))
for point in [(145, 40), (167, 56), (200, 88)]:
    pixel("clipped", point, clear)
for point in [(168, 56), (199, 87)]:
    pixel("clipped", point, (255, 0, 0))
pixel("copied", (176, 64), (255, 0, 0))
pixel("legacy-reset", (176, 64), (255, 0, 0))
pixel("legacy-reset", (288, 64), (0, 0, 255))
check(images["copied"].tobytes() == images["reloaded"].tobytes(), "reload preserves shader output")
for name in images:
    pixel(name, (900, 500), clear)
print(f"MATERIAL PIXELS PASS assertions={count}; software Vulkan readback")
