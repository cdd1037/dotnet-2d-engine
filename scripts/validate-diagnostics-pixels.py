#!/usr/bin/env python3
"""Validate the existing-asset debug geometry fixture; no screenshot generation."""
from pathlib import Path
import sys
from PIL import Image

root = Path(sys.argv[1] if len(sys.argv) > 1 else "evidence/diagnostics")
frames = [Image.open(root / f"frame-{i}.bmp").convert("RGB") for i in range(3)]
checks = 0


def check(condition, message):
    global checks
    if not condition:
        raise AssertionError(message)
    checks += 1


check(all(image.size == (960, 540) for image in frames), "fixture dimensions")
check(frames[0].tobytes() == frames[2].tobytes(), "re-enabled geometry matches initial frame")
clear, scene = (9, 11, 20), (64, 89, 178)
for point in [(200, 128), (128, 170), (200, 224), (288, 170)]:
    check(frames[0].getpixel(point) == (0, 255, 255), f"rectangle edge {point}")
check(frames[0].getpixel((100, 300)) == (255, 255, 0), "horizontal solid line")
check(frames[0].getpixel((460, 180)) == (255, 0, 255), "diagonal solid line")
for point in [(50, 50), (100, 295), (460, 190), (47, 300), (350, 300)]:
    check(frames[0].getpixel(point) == clear, f"outside geometry {point}")
for image in frames:
    check(image.getpixel((160, 160)) == scene, "outline keeps scene interior")
for point in [(100, 300), (460, 180), (200, 224), (288, 170)]:
    check(frames[1].getpixel(point) == clear, f"disabled overlay clears {point}")
check(frames[1].getpixel((200, 128)) == scene, "disabled overlay reveals scene")
print(f"DIAGNOSTICS PIXELS PASS assertions={checks}; software Vulkan readback")
