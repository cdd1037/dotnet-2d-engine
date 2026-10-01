#!/usr/bin/env python3
"""Validate deterministic --animation-scenario captures, using the existing Pillow test dependency."""
import sys
from pathlib import Path
from PIL import Image

root = Path(sys.argv[1] if len(sys.argv) > 1 else 'evidence/animation/visual')
frames = {i: Image.open(root / f'frame-{i:02d}.bmp').convert('RGB') for i in (0, 1, 2, 3, 7, 9, 10, 11)}
checks = 0

def check(ok, label):
    global checks
    if not ok:
        raise AssertionError(label)
    checks += 1

for frame in frames.values():
    check(frame.size == (960, 540), 'expected fixture viewport')
check(frames[0].getpixel((85, 165)) == (255, 0, 0), 'initial game atlas red corner')
check(frames[0].getpixel((165, 335)) == (255, 0, 0), 'real sprite flipped horizontally')
check(frames[2].getpixel((790, 210)) == (255, 0, 0), 'one-shot red frame')
check(frames[7].getpixel((790, 210)) == (0, 0, 255), 'one-shot game frame frozen blue')
check(frames[3].crop((0, 50, 960, 300)).tobytes() == frames[7].crop((0, 50, 960, 300)).tobytes(), 'game movement/rotation/frames unchanged during pause')
check(frames[3].crop((70, 320, 190, 440)).tobytes() != frames[7].crop((70, 320, 190, 440)).tobytes(), 'real-time tint advances during game pause')
check(frames[9].crop((0, 50, 960, 540)).tobytes() == frames[10].crop((0, 50, 960, 540)).tobytes(), 'cancel preserves visible values')
check(frames[1].tobytes() == frames[11].tobytes(), 'restart repeats the same rendered state')
check(frames[10].getpixel((25, 25)) == (255, 0, 0) and frames[11].getpixel((25, 25)) == (0, 0, 255), 'cancel and restart status')
print(f'ANIMATION PIXELS PASS assertions={checks}')
