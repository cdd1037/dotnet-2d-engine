#!/usr/bin/env python3
"""Optional regeneration of original synthetic audio fixtures. Requires existing ffmpeg
with libvorbis for encoding only; neither ffmpeg nor an encoder is a runtime dependency.
"""
import math
import struct
import subprocess
import tempfile
import wave
from pathlib import Path
root=Path(__file__).resolve().parents[1]
assets=root/'assets/audio'
assets.mkdir(parents=True,exist_ok=True)
def write(path,frames,sample):
    with wave.open(str(path),'wb') as output:
        output.setparams((2,2,48000,frames,'NONE','not compressed'))
        output.writeframes(b''.join(struct.pack('<hh',*sample(i)) for i in range(frames)))
write(assets/'pcm.wav',2048,lambda i:(8192,-8192))
def cue(i):
    v=round(10000*math.sin(2*math.pi*(660 if i<4320 else 880)*i/48000)*min(1,i/480)*max(0,1-i/8640))
    return v,v
write(assets/'cue.wav',8640,cue)
notes=[220,261.625565,329.627557,293.664768,261.625565,196]
def music(i):
    v=round(4000*math.sin(2*math.pi*notes[i//24000]*i/48000)*min(1,(i%24000)/480)*min(1,(23999-i%24000)/480))
    return v,v
with tempfile.TemporaryDirectory(prefix='gal-audio-fixture-') as temp:
    source=Path(temp)/'music.wav'
    write(source,144000,music)
    subprocess.run(['ffmpeg','-hide_banner','-loglevel','error','-y','-i',str(source),'-c:a','libvorbis','-q:a','3','-map_metadata','-1',str(assets/'music.ogg')],check=True)
print('Generated original PCM WAV, cue WAV and Ogg/Vorbis music fixtures')
