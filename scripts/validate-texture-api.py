#!/usr/bin/env python3
import ctypes as C,os,math
from pathlib import Path
from PIL import Image
root=Path(__file__).resolve().parents[1];out=root/'evidence/two-room';out.mkdir(exist_ok=True)
class Config(C.Structure):_fields_=[('size',C.c_uint),('abi',C.c_uint),('w',C.c_int),('h',C.c_int),('capacity',C.c_uint),('flags',C.c_uint)]
class Camera(C.Structure):_fields_=[('x',C.c_float),('y',C.c_float),('zoom',C.c_float)]
class Draw(C.Structure):_fields_=[(s,C.c_float) for s in ['m11','m12','m21','m22','x','y','w','h','r','g','b','a']]+[('texture',C.c_uint64)]
class Stats(C.Structure):_fields_=[(s,C.c_uint) for s in ['size','frames','sprites','draws','audio']]
lib=C.CDLL(str(root/'build/libgal.so'));lib.gal_last_error.restype=C.c_char_p
for name,args in {'gal_create':[C.POINTER(Config),C.POINTER(C.c_void_p)],'gal_destroy':[C.c_void_p],'gal_begin':[C.c_void_p,C.POINTER(Camera)],'gal_submit_draws':[C.c_void_p,C.POINTER(Draw),C.c_uint],'gal_end':[C.c_void_p],'gal_abort':[C.c_void_p],'gal_texture_load_bmp':[C.c_void_p,C.c_char_p,C.POINTER(C.c_uint64)],'gal_texture_release':[C.c_void_p,C.c_uint64],'gal_texture_count':[C.c_void_p,C.POINTER(C.c_uint)],'gal_get_stats':[C.c_void_p,C.POINTER(Stats)]}.items():getattr(lib,name).argtypes=args
assert C.sizeof(Draw)==56
os.environ['GAL_CAPTURE_BMP']=str(out/'texture-api.bmp');ctx=C.c_void_p();cfg=Config(C.sizeof(Config),1,160,100,16,0)
def ok(n):assert n==0,lib.gal_last_error().decode()
def count(n):v=C.c_uint();ok(lib.gal_texture_count(ctx,C.byref(v)));assert v.value==n
ok(lib.gal_create(C.byref(cfg),C.byref(ctx)));p=C.c_uint64();q=C.c_uint64()
try:
 assert lib.gal_texture_load_bmp(ctx,b'/definitely/missing.bmp',C.byref(p))==-1 and p.value==0;count(0)
 bad=out/'invalid.bmp';bad.write_bytes(b'not an image')
 assert lib.gal_texture_load_bmp(ctx,str(bad).encode(),C.byref(p))==-1;count(0)
 ok(lib.gal_texture_load_bmp(ctx,str(root/'assets/player.bmp').encode(),C.byref(p)));ok(lib.gal_texture_load_bmp(ctx,str(root/'assets/cell.bmp').encode(),C.byref(q)));count(2)
 draws=(Draw*3)(Draw(1,0,0,1,20,20,36,46,1,1,1,1,p.value),Draw(0,1,-1,0,100,40,28,32,1,1,1,1,q.value),Draw(1,0,0,1,120,20,20,24,1,1,1,1,p.value))
 camera=Camera(0,0,1);ok(lib.gal_begin(ctx,C.byref(camera)));ok(lib.gal_submit_draws(ctx,draws,3));assert lib.gal_texture_release(ctx,p)==-1;ok(lib.gal_end(ctx))
 stats=Stats();stats.size=C.sizeof(stats);ok(lib.gal_get_stats(ctx,C.byref(stats)));assert (stats.sprites,stats.draws)==(3,3) # ordered texture runs, no destructive sorting
 im=Image.open(out/'texture-api.bmp').convert('RGB');im.save(out/'texture-api.png')
 def near(at,rgb):assert max(abs(a-b) for a,b in zip(im.getpixel(at),rgb))<=4,(at,im.getpixel(at),rgb)
 near((20,20),(9,11,20));near((38,38),(38,74,93));near((84,54),(255,229,151))
 stale=p.value;ok(lib.gal_texture_release(ctx,p));count(1);assert lib.gal_texture_release(ctx,p)==-1
 ok(lib.gal_begin(ctx,C.byref(camera)));assert lib.gal_submit_draws(ctx,draws,3)==-1;ok(lib.gal_abort(ctx))
 # Context teardown releases the still-live second texture; stale IDs cannot cross contexts.
finally:ok(lib.gal_destroy(ctx))
os.environ.pop('GAL_CAPTURE_BMP',None);ok(lib.gal_create(C.byref(cfg),C.byref(ctx)))
try:
 count(0);fresh=C.c_uint64();ok(lib.gal_texture_load_bmp(ctx,str(root/'assets/player.bmp').encode(),C.byref(fresh)));assert fresh.value!=stale
 assert lib.gal_texture_release(ctx,stale)==-1;ok(lib.gal_texture_release(ctx,fresh));count(0)
finally:ok(lib.gal_destroy(ctx))
print('PASS real BMP upload/alpha, 90-degree affine pixel orientation, 3 ordered texture runs, missing/corrupt assets, active-frame release rejection, stale handles, context ownership/reset')
