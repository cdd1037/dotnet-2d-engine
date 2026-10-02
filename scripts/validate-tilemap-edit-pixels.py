#!/usr/bin/env python3
"""Validate three runtime TileMap edit readbacks; optionally compare JIT/AOT pixels.

Usage: validate-tilemap-edit-pixels.py [capture-directory] [comparison-directory]
Software-rendered readbacks do not constitute physical GPU/window acceptance.
"""
import os
import sys
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw


root = Path(sys.argv[1] if len(sys.argv) > 1 else os.environ.get(
    'GAL_TILEMAP_EDIT_CAPTURE_DIR', 'evidence/tilemap-edit/visual-jit'))
frames = [Image.open(root / f'frame-{i:03d}.bmp').convert('RGB') for i in range(3)]
checks = 0
background = (9, 11, 20)
red, yellow, magenta, white = (255, 0, 0), (255, 255, 0), (255, 0, 255), (255, 255, 255)
blue, cyan, green, black = (0, 0, 255), (0, 255, 255), (0, 255, 0), (0, 0, 0)


def check(condition, label):
    global checks
    if not condition:
        raise AssertionError(label)
    checks += 1


def rectangle(x, y, origin_x=32):
    left, top = origin_x + x * 24, 32 + y * 24
    return left, top, left + 24, top + 24


def solid(image, x, y, color, label, origin_x=32):
    patch = image.crop(rectangle(x, y, origin_x))
    check(patch.tobytes() == Image.new('RGB', (24, 24), color).tobytes(), label)


def corners(image, x, y, colors, label, origin_x=32):
    left, top, _, _ = rectangle(x, y, origin_x)
    for (dx, dy), color in zip(((2, 2), (21, 2), (2, 21), (21, 21)), colors):
        check(image.getpixel((left + dx, top + dy)) == color, f'{label} corner {dx},{dy}')


def outside_clear(image, cells):
    mask = Image.new('L', image.size, 255)
    draw = ImageDraw.Draw(mask)
    for origin_x, x, y in cells:
        left, top, right, bottom = rectangle(x, y, origin_x)
        draw.rectangle((left, top, right - 1, bottom - 1), fill=0)
    clear = Image.new('RGB', image.size, background)
    check(ImageChops.difference(Image.composite(image, clear, mask), clear).getbbox() is None,
          'all pixels outside occupied cells are background')


original_cells = [(15, 14), (14, 15), (15, 15)]
edited_cells = [(15, 14), (14, 15), (16, 15), (15, 16)]
control_cells = [(496, x, y) for x, y in original_cells]
for index, frame in enumerate(frames):
    check(frame.size == (960, 540), f'frame {index} dimensions')
    active = edited_cells if index == 1 else original_cells
    outside_clear(frame, [(32, x, y) for x, y in active] + control_cells)
    solid(frame, 15, 14, red, f'frame {index} sibling replacement target unchanged', 496)
    solid(frame, 15, 15, red, f'frame {index} sibling removal target unchanged', 496)
    corners(frame, 14, 15, (red, yellow, magenta, white), f'frame {index} sibling flags unchanged', 496)
    check(frame.crop((496, 32, 928, 464)).tobytes()
          == frames[0].crop((496, 32, 928, 464)).tobytes(), f'frame {index} complete sibling map unchanged')

solid(frames[0], 15, 14, red, 'initial replacement target')
solid(frames[0], 15, 15, red, 'initial removal target')
corners(frames[0], 14, 15, (red, yellow, magenta, white), 'initial marker')
solid(frames[0], 16, 15, background, 'right chunk initially empty')
solid(frames[0], 15, 16, background, 'bottom chunk initially empty')

solid(frames[1], 15, 14, blue, 'replacement uses a previously unused atlas alias')
solid(frames[1], 15, 15, background, 'removed tile has no stale pixels')
corners(frames[1], 14, 15, (yellow, red, white, magenta), 'X flip changes existing cell')
corners(frames[1], 16, 15, (green, black, blue, cyan), 'new right chunk decor has Y flip')
corners(frames[1], 15, 16, (white, magenta, yellow, red), 'new bottom chunk marker has XY flip')
check(frames[1].crop(rectangle(14, 15)).tobytes()
      == frames[0].crop(rectangle(14, 15)).transpose(Image.Transpose.FLIP_LEFT_RIGHT).tobytes(),
      'whole existing tile is horizontally mirrored')
check(frames[0].tobytes() != frames[1].tobytes(), 'edit changes actual rendered pixels')
check(frames[2].tobytes() == frames[0].tobytes(), 'restore reproduces exact original frame')
solid(frames[2], 16, 15, background, 'emptied right chunk clears all pixels')
solid(frames[2], 15, 16, background, 'emptied bottom chunk clears all pixels')

if len(sys.argv) > 2:
    comparison = Path(sys.argv[2])
    for index, frame in enumerate(frames):
        other = Image.open(comparison / f'frame-{index:03d}.bmp').convert('RGB')
        check(frame.size == other.size and frame.tobytes() == other.tobytes(),
              f'frame {index} JIT/AOT readbacks match exactly')

print(f'TILEMAP EDIT PIXELS PASS assertions={checks}; add/remove/replace, X/Y/XY flips, '
      '15/16 chunk axes, shared-source isolation, exact restore')
