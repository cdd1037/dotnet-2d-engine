#!/usr/bin/env python3
"""Actual SDL_GPU rendering/readback under the explicitly selected Mesa software ICD.
Requires local SDL/Mesa packages and Pillow. This is not a physical GPU/window/input test.
"""
import ctypes as C
import os
from pathlib import Path
from PIL import Image
root=Path(__file__).resolve().parents[1]
out=root/'evidence/graphics';out.mkdir(parents=True,exist_ok=True)
class Config(C.Structure): _fields_=[('size',C.c_uint),('abi',C.c_uint),('width',C.c_int),('height',C.c_int),('capacity',C.c_uint),('flags',C.c_uint)]
class Camera(C.Structure): _fields_=[('x',C.c_float),('y',C.c_float),('zoom',C.c_float)]
class Sprite(C.Structure): _fields_=[(x,C.c_float) for x in ('x','y','w','h','r','g','b','a')]
class Input(C.Structure): _fields_=[('size',C.c_uint),('quit',C.c_uint),('keys',C.c_uint),('wheel',C.c_float),('mx',C.c_float),('my',C.c_float),('width',C.c_int),('height',C.c_int)]
class Stats(C.Structure): _fields_=[(x,C.c_uint) for x in ('size','frames','sprites','draws','audio')]
lib=C.CDLL(str(root/'build/libgal.so'));sdl=C.CDLL(str(root/'.deps/sdl-install/lib/libSDL3.so'))
for name,args in {'gal_create':[C.POINTER(Config),C.POINTER(C.c_void_p)],'gal_destroy':[C.c_void_p],'gal_begin':[C.c_void_p,C.POINTER(Camera)],'gal_submit':[C.c_void_p,C.POINTER(Sprite),C.c_uint],'gal_end':[C.c_void_p],'gal_poll':[C.c_void_p,C.POINTER(Input)],'gal_get_stats':[C.c_void_p,C.POINTER(Stats)],'gal_play_tone':[C.c_void_p]}.items():
 f=getattr(lib,name);f.argtypes=args;f.restype=C.c_int
lib.gal_last_error.restype=C.c_char_p
sdl.SDL_GetWindows.argtypes=[C.POINTER(C.c_int)];sdl.SDL_GetWindows.restype=C.POINTER(C.c_void_p)
sdl.SDL_SetWindowSize.argtypes=[C.c_void_p,C.c_int,C.c_int];sdl.SDL_SetWindowSize.restype=C.c_bool
sdl.SDL_free.argtypes=[C.c_void_p]
def ok(value):
 assert value==0,lib.gal_last_error().decode()
def frame(name,sprites,camera=Camera(0,0,1),resize=None,tone=False):
 os.environ['GAL_CAPTURE_BMP']=str(out/f'{name}.bmp')
 cfg=Config(C.sizeof(Config),1,256,192,16,2 if tone else 0);ctx=C.c_void_p();ok(lib.gal_create(C.byref(cfg),C.byref(ctx)))
 try:
  if resize:
   count=C.c_int();windows=sdl.SDL_GetWindows(C.byref(count));assert count.value==1
   window=windows[0];sdl.SDL_free(windows);assert sdl.SDL_SetWindowSize(window,*resize)
  inp=Input();inp.size=C.sizeof(inp);ok(lib.gal_poll(ctx,C.byref(inp)))
  if resize:assert (inp.width,inp.height)==resize
  ok(lib.gal_begin(ctx,C.byref(camera)));items=(Sprite*len(sprites))(*sprites);ok(lib.gal_submit(ctx,items,len(items)));ok(lib.gal_end(ctx))
  if tone:ok(lib.gal_play_tone(ctx))
  stats=Stats();stats.size=C.sizeof(stats);ok(lib.gal_get_stats(ctx,C.byref(stats)))
  assert (stats.frames,stats.sprites,stats.draws,stats.audio)==(1,len(sprites),1,int(tone))
 finally:ok(lib.gal_destroy(ctx))
 im=Image.open(out/f'{name}.bmp').convert('RGB');im.save(out/f'{name}.png');return im
red=Sprite(32,32,80,80,1,0,0,.5);green=Sprite(32,32,80,80,0,1,0,.5)
a=frame('alpha-red-green',[red,green]);b=frame('alpha-green-red',[green,red])
def near(pixel,expected):assert all(abs(x-y)<=3 for x,y in zip(pixel,expected)),(pixel,expected)
near(a.getpixel((72,72)),(66,130,5));near(b.getpixel((72,72)),(130,66,5))
near(a.getpixel((0,0)),(9,11,20));near(a.getpixel((33,33)),(9,11,20)) # transparent texture corners
blue=Sprite(80,64,32,32,0,0,1,1)
c=frame('camera-pan-zoom',[blue],Camera(64,48,2));near(c.getpixel((64,64)),(0,0,255));near(c.getpixel((96,80)),(9,11,20))
d=frame('surface-resize',[blue],resize=(384,288),tone=True);assert d.size==(384,288);near(d.getpixel((96,80)),(0,0,255))
print('PASS SDL_GPU + Mesa software Vulkan: textured-alpha pixels, reversed blend order, transparent corners, camera pan/zoom, 384x288 offscreen surface resize, 4 real draws')
print('PASS dummy SDL audio stream accepts one tone; audio counter=1 (not audible-output validation)')
print('NOT TESTED: displayed window, physical keyboard/mouse, minimize/restore, physical GPU/driver performance')
