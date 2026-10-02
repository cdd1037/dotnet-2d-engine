#!/usr/bin/env python3
"""Check RELAY's functional results and rendered screens."""
from pathlib import Path
from PIL import Image
import json,re,sys,xml.etree.ElementTree as ET
p=Path(sys.argv[1])
project=ET.parse(p/'app/Sample.csproj')
assert not project.findall('.//ProjectReference') and not project.findall('.//Compile')
assert {r.attrib['Include'] for r in project.findall('.//PackageReference')}=={'Dotnet2D.Engine','Dotnet2D.Native.Linux.x64'}
modes=[mode for mode in ('jit','trim','aot') if (p/'publish'/mode).exists()]
for mode in modes:
 for log in (p/'logs').glob(mode+'-*'):
  if log.name.endswith(('-build.log','-publish.log','-restore.log')):assert not re.search(r'\b(?:warning|error) [A-Z]+\d+',log.read_text()),log
 for package in ('Dotnet2D.Engine','Dotnet2D.Native.Linux.x64'):
  assert (p/'publish'/mode/'licenses'/package/'LICENSE.txt').is_file(),(mode,package)
screens=('title','paused','archive','won','lost','final-title'); pixels=[]; checks=0
for screen in screens:
 imgs=[Image.open(p/'captures'/mode/(screen+'.bmp')).convert('RGBA') for mode in modes]
 assert all(im.size==(960,540) for im in imgs);checks+=1
 assert all(imgs[0].tobytes()==im.tobytes() for im in imgs[1:]);checks+=1
 assert len(imgs[0].getcolors(maxcolors=960*540))>500;checks+=1
 pixels.append(imgs[0].tobytes())
assert len(set(pixels))==len(screens);checks+=1
for screen,point,color in [('title',(230,110),(23,35,56)),('archive',(500,180),(73,62,56)),('lost',(560,375),(46,80,100))]:
 assert Image.open(p/'captures/jit'/(screen+'.bmp')).convert('RGB').getpixel(point)==color;checks+=1
results={}
for mode in modes:
 text=(p/f'logs/{mode}-run.log').read_text()
 rules=re.search(r'RELAY RULE CHECK PASS assertions=(\d+)',text);scenario=re.search(r'RELAY SCENARIO PASS assertions=(\d+)',text)
 assert rules and scenario and 'Previous run kept' in text
 results[mode]={'rule_assertions':int(rules[1]),'scenario_assertions':int(scenario[1])}
assert len({(r['rule_assertions'],r['scenario_assertions']) for r in results.values()})==1
(p/'results.json').write_text(json.dumps({'modes':results,'pixel_assertions':checks,'physical_device_acceptance':False},indent=2)+'\n')
print(f'RELAY PIXELS PASS assertions={checks}; modes={",".join(modes)}')
print(json.dumps(results,indent=2))
