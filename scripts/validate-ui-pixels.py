from pathlib import Path
from PIL import Image, ImageChops
p=Path('evidence/ui')
for mode in ('jit','aot'):
    for stage in ('settings','settings.initial'):
        im=Image.open(p/f'{mode}-{stage}.bmp').convert('RGB')
        assert im.size==(960,540)
        im.save(p/f'{mode}-{stage}.png')
    a=Image.open(p/f'{mode}-settings.initial.png')
    b=Image.open(p/f'{mode}-settings.png')
    # Scroll alters content inside its rectangular viewport, not its frame or the room.
    diff=ImageChops.difference(a,b)
    changes=sum(any(pixel) for pixel in diff.crop((44,328,499,438)).getdata())
    assert changes>1000,changes
    assert diff.crop((44,442,510,456)).getbbox() is None # below scroll viewport
    assert diff.crop((535,0,960,540)).getbbox() is None # world outside panel unchanged
    assert b.getpixel((30,25)) != b.getpixel((900,300)) # UI/world composited into one target
    assert len(set(b.crop((44,104,220,120)).getdata()))>20 # actual antialiased bilingual glyphs
    print(f'PASS {mode}: scroll pixel changes={changes}, rectangular clipping/background preserved, antialiased text')
for stage in ('settings','settings.initial'):
    assert ImageChops.difference(Image.open(p/f'jit-{stage}.png'),Image.open(p/f'aot-{stage}.png')).getbbox() is None
print('PASS JIT/AOT UI pixels identical (initial and changed/scrolled checkpoints); actual software Vulkan readback')
