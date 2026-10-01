#!/usr/bin/env python3
"""Original procedural BMP art; no external assets, services or runtime font dependency."""
from PIL import Image,ImageDraw,ImageFont
from pathlib import Path
import struct
root=Path(__file__).resolve().parents[1];out=root/'assets';out.mkdir(exist_ok=True)
def font(n,bold=False):return ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSans'+('-Bold' if bold else '')+'.ttf',n)
def bmp(im,name):
 im=im.convert('RGBA');w,h=im.size;pixels=im.tobytes('raw','BGRA',0,-1)
 header=struct.pack('<IiiHHIIiiII',108,w,h,1,32,3,len(pixels),2835,2835,0,0)+struct.pack('<IIII',0xff0000,0xff00,0xff,0xff000000)+b'\0'*52
 (out/(name+'.bmp')).write_bytes(struct.pack('<2sIHHI',b'BM',122+len(pixels),0,0,122)+header+pixels)
for room in range(2):
 im=Image.new('RGBA',(960,540),'#0d1723');d=ImageDraw.Draw(im)
 accent='#5ad6b5' if room==0 else '#ffca80';base='#24453f' if room==0 else '#493e38';grid='#2b5149' if room==0 else '#56463e'
 d.text((40,24),'RELAY',font=font(28,True),fill='#f0f6f7');d.text((158,31),'A TWO-ROOM ENGINE SAMPLE',font=font(12),fill='#8c9baa')
 d.line((40,73,920,73),fill='#2a3747',width=1);d.text((42,85),('01  /  GARDEN WORKSHOP' if room==0 else '02  /  FIELD ARCHIVE'),font=font(15,True),fill=accent)
 for i in range(2):
  x=766+i*78;d.rounded_rectangle((x,24,x+66,57),8,fill=accent if room==i else '#1c2a38');d.text((x+21,31),'0'+str(i+1),font=font(14,True),fill='#13231f' if room==i else '#647789')
 d.rounded_rectangle((46,120,914,474),18,fill='#080f18');d.rectangle((64,132,896,456),fill=base)
 for x in range(64,897,32):d.line((x,132,x,456),fill=grid)
 for y in range(132,457,32):d.line((64,y,896,y),fill=grid)
 d.rectangle((48,116,912,132),fill='#516562' if room==0 else '#776253');d.rectangle((48,456,912,472),fill='#253239');d.rectangle((48,132,64,456),fill='#344842');d.rectangle((896,132,912,456),fill='#344842')
 doorx=896 if room==0 else 48;d.rectangle((doorx,258,doorx+16,338),fill=accent);d.rectangle((doorx-6,258,doorx+22,266),fill='#111c26');d.rectangle((doorx-6,330,doorx+22,338),fill='#111c26')
 if room==0:d.polygon([(851,286),(865,299),(851,312)],fill=accent)
 else:d.polygon([(108,286),(94,299),(108,312)],fill=accent)
 for x,y,w,h in [(400,190,90,100),(610,350,100,60)]:
  d.rounded_rectangle((x+6,y+8,x+w+6,y+h+8),6,fill='#142b27' if room==0 else '#2e2424');d.rounded_rectangle((x,y,x+w,y+h),6,fill='#59705e' if room==0 else '#897157',outline='#93aa83' if room==0 else '#b29671',width=2)
  for k in range(12,w-10,14):d.line((x+k,y+8,x+k,y+h-8),fill='#3f5749' if room==0 else '#65533f',width=2)
  d.rectangle((x+8,y+10,x+w-8,y+20),fill='#819077' if room==0 else '#aa8c66')
 d.text((74,425),'GROW / REPAIR / CARRY' if room==0 else 'KEEP / RECORD / RETURN',font=font(11),fill='#68877a' if room==0 else '#967f69')
 d.text((44,493),'WASD  MOVE     E  PICK UP     F  DROP     T  USE DOOR',font=font(12,True),fill='#b3c3ce');d.text((716,493),'F5 SAVE   F9 LOAD',font=font(12),fill='#718596')
 bmp(im,'room-a' if room==0 else 'room-b')
im=Image.new('RGBA',(36,46));d=ImageDraw.Draw(im);d.ellipse((3,32,34,45),fill=(0,0,0,65));d.rounded_rectangle((6,20,29,39),5,fill='#4cbdce',outline='#b4f3ef',width=2);d.rectangle((10,35,15,43),fill='#1a344a');d.rectangle((23,35,28,43),fill='#1a344a');d.rounded_rectangle((4,4,31,25),10,fill='#e2ece4',outline='#577b80',width=2);d.rounded_rectangle((8,10,28,21),5,fill='#264a5d');d.rectangle((19,12,25,15),fill='#b8fcf3');bmp(im,'player')
im=Image.new('RGBA',(28,32));d=ImageDraw.Draw(im);d.rounded_rectangle((3,3,25,29),5,fill='#e9a943',outline='#ffe9a7',width=2);d.rectangle((8,0,20,5),fill='#60594d');d.rectangle((8,8,20,24),fill='#614328');d.polygon([(16,9),(11,18),(16,18),(13,24),(20,15),(15,15)],fill='#ffe597');bmp(im,'cell')
for key,text,color in [('status-empty','FIND THE POWER CELL','#8fa6b6'),('status-held','CELL LINKED TO PLAYER','#f4ce7e'),('status-restored','SAVE RESTORED / SAME ID','#80dbc4')]:
 im=Image.new('RGBA',(248,24));d=ImageDraw.Draw(im);d.rounded_rectangle((0,0,247,23),7,fill='#1b2b38');d.ellipse((9,9,15,15),fill=color);d.text((23,5),text,font=font(10,True),fill=color);bmp(im,key)
print('Generated original BMP assets:',len(list(out.glob('*.bmp'))))
