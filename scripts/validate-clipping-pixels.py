#!/usr/bin/env python3
"""Validate GPU readbacks, keeping framebuffer scissor separate from tile culling/UI clipping."""
import os
from pathlib import Path
from PIL import Image,ImageChops,ImageDraw
root=Path(os.environ.get('GAL_CLIP_CAPTURE_DIR','evidence/clipping/jit'))
def load(name):return Image.open(root/(name+'.bmp')).convert('RGB')
checks=0
background=(9,11,20)
def near(image,point,color):
 global checks
 actual=image.getpixel(point);assert max(abs(a-b) for a,b in zip(actual,color))<=3,(point,actual,color);checks+=1
def outside_clear(image,rectangles):
 global checks
 mask=Image.new('L',image.size,255);draw=ImageDraw.Draw(mask)
 for x,y,w,h in rectangles:
  if w and h:draw.rectangle((x,y,x+w-1,y+h-1),fill=0)
 clear=Image.new('RGB',image.size,background)
 assert ImageChops.difference(Image.composite(image,clear,mask),clear).getbbox() is None,'pixels escaped scissor'
 checks+=1
full=load('regions-full');clipped=load('regions-clipped');assert full.size==clipped.size==(960,540)
for p,c in { (48,48):(255,0,0),(63,63):(255,0,0),(64,64):(128,0,128),(111,111):(128,0,128),
 (112,112):(4,5,138),(127,127):(4,5,138),(47,48):background,(128,100):background,
 (160,40):(0,255,0),(179,59):(0,255,0),(180,59):background }.items():near(clipped,p,c)
outside_clear(clipped,[(48,48,64,64),(64,64,64,64),(160,40,20,20),(224,64,64,64)])
assert ImageChops.difference(full.crop((224,64,288,128)),clipped.crop((224,64,288,128))).getbbox() is None;checks+=1
same=load('same-clip');outside_clear(same,[(48,48,64,64)]);near(same,(64,64),(128,0,128))
empty=load('empty');outside_clear(empty,[])
legacy=load('legacy-reset');near(legacy,(160,40),(0,255,0));near(legacy,(64,64),(128,0,128));outside_clear(legacy,[(48,48,64,64),(64,64,64,64),(160,40,20,20)])
tile_full=load('tile-full');tile_clip=load('tile-clipped');outside_clear(tile_clip,[(77,101,319,247)])
assert ImageChops.difference(tile_full.crop((77,101,396,348)),tile_clip.crop((77,101,396,348))).getbbox() is None;checks+=1
assert ImageChops.difference(tile_clip,Image.new('RGB',tile_clip.size,background)).getbbox() is not None;checks+=1
assert load('ui-full').tobytes()==load('ui-empty-world').tobytes();checks+=1
resized=load('resized');assert resized.size==(376,244);outside_clear(resized,[(0,20,376,40)])
for p,c in {(0,20):(255,0,0),(375,59):(255,0,0),(0,19):background,(100,60):background}.items():near(resized,p,c)
print(f'CLIP PIXELS PASS assertions={checks}; exact half-open edges, alpha order, atlas/rotation, legacy reset, tile crop, UI isolation, resize')
