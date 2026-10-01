#!/usr/bin/env python3
"""Check the five-camera tilemap fixture and optional real-solver captures."""
import sys
from pathlib import Path
from PIL import Image

root=Path(sys.argv[1] if len(sys.argv)>1 else 'evidence/tilemap/visual-jit')
frames={i:Image.open(root/f'frame-{i:03d}.bmp').convert('RGB') for i in range(5)}
count=0

def check(ok,label):
    global count
    if not ok: raise AssertionError(label)
    count+=1

background=(9,11,20)
for im in frames.values(): check(im.size==(960,540),'fixture dimensions')
check(frames[0].tobytes()==frames[4].tobytes(),'camera reset reproduces exact frame')
check(frames[0].getpixel((63,496))==background and frames[0].getpixel((64,496))==(255,0,0),'placed grid left edge')
check(frames[0].getpixel((959,496))==(255,0,0),'partially visible final cell reaches framebuffer edge')
check(frames[0].getpixel((180,324))==(128,128,127),'source-order alpha layer over blue wall')
check(frames[0].getpixel((120,484))==(255,128,0),'source-order alpha layer over red floor')
check(frames[0].getpixel((100,132))==(2,2,4),'both flip flags select dark atlas corner with layer opacity')
check(frames[1].getpixel((927,496))==(255,0,0) and frames[1].getpixel((928,496))==background,'panned right half-open map edge')
check(frames[2].getpixel((540,400))==(255,0,0) and frames[2].getpixel((527,400))==background and frames[2].getpixel((864,400))==background,'zoomed platform boundaries')
check(frames[3].getpixel((96,390))==(255,0,0) and frames[3].getpixel((911,390))==(255,0,0) and frames[3].getpixel((912,390))==background,'negative camera and zoomed-out map edges')
check(frames[3].getpixel((100,484))==(255,0,0),'second chunk-row geometry becomes visible')
if len(sys.argv)>2:
    physics=Path(sys.argv[2]); initial=Image.open(physics/'frame-000.bmp').convert('RGB'); final=Image.open(physics/'frame-179.bmp').convert('RGB')
    check(initial.getpixel((784,128))==(76,255,128),'initial dynamic circle above floor')
    check(final.getpixel((784,467))==(76,255,128),'dynamic circle rests over generated floor')
    check(final.getpixel((784,496))==(255,0,0),'static floor preserved after solver steps')
print(f'TILEMAP PIXELS PASS assertions={count}')
