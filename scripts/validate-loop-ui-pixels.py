#!/usr/bin/env python3
"""Verify sprite pose/color and the visible menu in actual software-rendered BMPs."""
import csv
import os
from pathlib import Path
from PIL import Image

root = Path(os.environ['GAL_LOOP_CAPTURE_DIR'])
checks = 0


def check(value, label):
    global checks
    assert value, label
    checks += 1


def near(actual, expected):
    return max(abs(a-b) for a, b in zip(actual, expected)) <= 3


with (root / 'captures.tsv').open() as stream:
    rows = list(csv.DictReader(stream, delimiter='\t'))
check({row['name'] for row in rows} == {'moving.bmp', 'paused.bmp', 'restarted.bmp'}, 'three required captures')
for row in rows:
    image = Image.open(root / row['name']).convert('RGB')
    check(image.size == (960, 540), row['name'] + ' framebuffer dimensions')
    x, y = float(row['renderX']), float(row['renderY'])
    rgb = tuple(int(row[key]) for key in ('red', 'green', 'blue'))
    check(near(image.getpixel((int(x), int(y))), rgb), row['name'] + ' sprite center color')
    # The menu is to the right of x=640; inspect only the scene here.
    points = [(px, py) for py in range(image.height) for px in range(640)
              if near(image.getpixel((px, py)), rgb)]
    check(len(points) == 32 * 32, row['name'] + ' exactly one unscaled 32x32 sprite')
    bounds = (min(px for px, py in points), min(py for px, py in points),
              max(px for px, py in points) + 1, max(py for px, py in points) + 1)
    expected = (x-16, y-16, x+16, y+16)
    check(all(abs(a-b) <= 1 for a, b in zip(bounds, expected)), (row['name'], bounds, expected))
    check(bounds[2]-bounds[0] == 32 and bounds[3]-bounds[1] == 32, row['name'] + ' exact extents')
    background = image.getpixel((2, 2))
    check(all(image.getpixel((int(x + dx), int(y + dy))) == background
              for dx, dy in ((-18, 0), (18, 0), (0, -18), (0, 18))), row['name'] + ' clear outside sprite')
    menu = image.crop((640, 0, 960, 540))
    colors = menu.getcolors(menu.width * menu.height)
    check(len(colors) > 20 and sum(n for n, color in colors if color != background) > 1000,
          row['name'] + ' visible menu with glyph/control pixels')
    if row['paused'] == 'true':
        check(abs(float(row['simX']) - x) < .001 and abs(float(row['simY']) - y) < .001,
              row['name'] + ' paused/restarted display equals authoritative pose')
print(f'LOOP UI PIXELS PASS assertions={checks}; three sprite poses/colors and visible pause menu; software readback only')
