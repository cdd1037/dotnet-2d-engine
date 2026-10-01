#!/usr/bin/env python3
"""Check captured authored atlas pixels, then v2 camera/resize using the same SDL runtime.
Software Vulkan only; this is not physical GPU or displayed-window acceptance.
"""
import ctypes as C
import os
from pathlib import Path
from PIL import Image
root = Path(__file__).resolve().parents[1]
out = root / 'evidence/regions'
fixture = Path(os.environ.get('GAL_REGION_CAPTURE', out / 'fixture.bmp'))
im = Image.open(fixture).convert('RGB')
assert im.size == (960, 540)
def near(image, at, expected):
    actual = image.getpixel(at)
    assert max(abs(a-b) for a,b in zip(actual,expected)) <= 3, (at, actual, expected)
checks = {
 (32,32):(255,0,0), (159,32):(255,255,0), (32,159):(255,0,255), (159,159):(255,255,255),
 (192,32):(0,0,255), (319,32):(0,255,255), (192,159):(0,255,0), (319,159):(0,0,0),
 (352,32):(255,255,0), (479,32):(255,0,0), (352,159):(255,255,255), (479,159):(255,0,255),
 (512,32):(255,0,255), (639,32):(255,255,255), (512,159):(255,0,0), (639,159):(255,255,0),
 (191,208):(255,0,0), (64,208):(255,0,255), (191,335):(255,255,0), (64,335):(255,255,255),
 (256,208):(0,0,128), (383,208):(0,128,128), (256,335):(0,128,0),
 (512,272):(66,2,133), (0,0):(9,11,20), (160,32):(9,11,20),
}
for at,color in checks.items(): near(im,at,color)
im.save(fixture.with_suffix('.png'))

class Config(C.Structure): _fields_=[('size',C.c_uint),('abi',C.c_uint),('w',C.c_int),('h',C.c_int),('capacity',C.c_uint),('flags',C.c_uint)]
class Camera(C.Structure): _fields_=[(x,C.c_float) for x in ('x','y','zoom')]
class Draw(C.Structure): _fields_=[(x,C.c_float) for x in ('m11','m12','m21','m22','x','y','w','h','r','g','b','a')]+[('texture',C.c_uint64)]
class DrawV2(C.Structure): _fields_=[('size',C.c_uint),('version',C.c_uint),('draw',Draw)]+[(x,C.c_int) for x in ('sx','sy','sw','sh')]+[('flags',C.c_uint),('reserved',C.c_uint)]
class Input(C.Structure): _fields_=[('size',C.c_uint),('version',C.c_uint),('quit',C.c_uint),('flags',C.c_uint)]+[(x,C.c_int) for x in ('ww','wh','pw','ph')]+[('tail',C.c_byte*440)]
lib=C.CDLL(str(root/'build/libgal.so'));sdl=C.CDLL('libSDL3.so.0');lib.gal_last_error.restype=C.c_char_p
for name,args in {'gal_create':[C.POINTER(Config),C.POINTER(C.c_void_p)],'gal_destroy':[C.c_void_p],'gal_begin':[C.c_void_p,C.POINTER(Camera)],'gal_submit_draws_v2':[C.c_void_p,C.POINTER(DrawV2),C.c_uint],'gal_end':[C.c_void_p],'gal_poll_v2':[C.c_void_p,C.POINTER(Input)],'gal_texture_load_bmp':[C.c_void_p,C.c_char_p,C.POINTER(C.c_uint64)]}.items():getattr(lib,name).argtypes=args
sdl.SDL_GetWindows.argtypes=[C.POINTER(C.c_int)];sdl.SDL_GetWindows.restype=C.POINTER(C.c_void_p)
sdl.SDL_SetWindowSize.argtypes=[C.c_void_p,C.c_int,C.c_int];sdl.SDL_SetWindowSize.restype=C.c_bool
sdl.SDL_free.argtypes=[C.c_void_p]
def ok(value): assert value==0,lib.gal_last_error().decode()
assert C.sizeof(DrawV2)==88 and C.sizeof(Input)==472
capture=out/'resize-camera.bmp';os.environ['GAL_CAPTURE_BMP']=str(capture)
ctx=C.c_void_p();cfg=Config(C.sizeof(Config),1,64,64,4,0);ok(lib.gal_create(C.byref(cfg),C.byref(ctx)))
try:
    n=C.c_int();windows=sdl.SDL_GetWindows(C.byref(n));assert n.value==1
    window=windows[0];sdl.SDL_free(windows);assert sdl.SDL_SetWindowSize(window,96,80)
    inp=Input();inp.size=C.sizeof(inp);inp.version=2;ok(lib.gal_poll_v2(ctx,C.byref(inp)));assert (inp.pw,inp.ph)==(96,80)
    texture=C.c_uint64();ok(lib.gal_texture_load_bmp(ctx,str(root/'assets/regions.bmp').encode(),C.byref(texture)))
    d=DrawV2(C.sizeof(DrawV2),2,Draw(1,0,0,1,5,6,8,8,1,1,1,1,texture.value),0,0,1,1,3,0)
    camera=Camera(1,2,2);ok(lib.gal_begin(ctx,C.byref(camera)));ok(lib.gal_submit_draws_v2(ctx,C.byref(d),1));ok(lib.gal_end(ctx))
finally: ok(lib.gal_destroy(ctx))
resized=Image.open(capture).convert('RGB');assert resized.size==(96,80)
near(resized,(8,8),(255,0,0));near(resized,(23,23),(255,0,0));near(resized,(24,8),(9,11,20))
print(f'PASS {len(checks)+3} region pixel checks: adjacent edges, X/Y flip, rotation, tint, stable alpha order, single-texel region, camera/resize')
