#!/usr/bin/env python3
"""Measure produced files; do not equate native package selection with native function trimming."""
import json, hashlib, sys
from pathlib import Path
from zipfile import ZipFile
proof=Path(sys.argv[1]).resolve()
native_package=next((proof/'feed').glob('Dotnet2D.Native.Linux.x64.*.nupkg'))
with ZipFile(native_package) as package:
    native_entries=[n for n in package.namelist() if n.startswith('runtimes/linux-x64/native/') and not n.endswith('/')]
    expected_native={Path(n).name:hashlib.sha256(package.read(n)).hexdigest() for n in native_entries}
    expected_native_bytes=sum(package.getinfo(n).file_size for n in native_entries)
rows=[]
for sample in ('empty','sprite','ui'):
    for mode in ('fdd','trim','aot'):
        directory=proof/'publish'/f'{sample}-{mode}'
        files=[p for p in directory.rglob('*') if p.is_file()]
        app=[p for p in files if p.name in (f'Sample.{sample}',f'Sample.{sample}.dll')]
        native=[p for p in files if p.name.startswith(('libgal.so','libSDL3.so','libfreetype.so','libpng16.so','libz.so','libbz2.so','libbrotlidec.so','libbrotlicommon.so'))]
        assets=[p for p in files if 'assets' in p.relative_to(directory).parts]
        symbols=[p for p in files if p.suffix in ('.pdb','.dbg')]
        managed=[p for p in files if p.name=='Dotnet2D.Engine.dll']
        metadata=[p for p in files if p.name.endswith(('.deps.json','.runtimeconfig.json'))]
        assigned=set(app+native+assets+symbols+managed+metadata)
        other=[p for p in files if p not in assigned]
        total=lambda group:sum(p.stat().st_size for p in group)
        rows.append(dict(sample=sample,mode=mode,app_bytes=total(app),engine_managed_bytes=total(managed),native_payload_bytes=total(native),asset_bytes=total(assets),metadata_bytes=total(metadata),symbols_bytes=total(symbols),framework_other_bytes=total(other),distribution_without_symbols_bytes=total(files)-total(symbols),native_files=len(native)))
        if sample=='empty': assert not native and not assets,(sample,mode,'unexpected native/assets')
        else:
            assert len(native)==len(expected_native) and total(native)==expected_native_bytes,(sample,mode,'native closure changed',total(native))
            assert all(hashlib.sha256(p.read_bytes()).hexdigest()==expected_native[p.name] for p in native),(sample,mode,'native asset changed')
        if sample=='sprite': assert len(assets)==2,(sample,mode,'sourcegen fixture files missing')
        if sample=='ui': assert len(assets)==2,(sample,mode,'UI source files missing')
records=[json.loads(line) for line in (proof/'logs/managed-types.jsonl').read_text().splitlines() if line.startswith('{')]
assert len(records)==3
assert not records[0]['exists'],'unused engine assembly should trim entirely'
for record in records[1:]:
    types=set(record['types'])
    for forbidden in ('GameAuthoringLab.AudioSession','GameAuthoringLab.PhysicsWorld','GameAuthoringLab.MissionGame','GameAuthoringLab.RoomGame','GameAuthoringLab.Program','GameAuthoringLab.SelfTests'):
        assert forbidden not in types,(record['path'],forbidden,'unexpected managed root')
assert not any('BoundUi' in t or 'UiAuthoring' in t for t in records[1]['types']),'sprite consumer retained UI'
assert any('BoundUiSession' in t for t in records[2]['types']),'UI consumer lost binding root'
# AOT symbol maps, unlike text search in stripped executables, identify compiled engine methods.
aot={}
for sample in ('empty','sprite','ui'):
    text=(proof/'logs'/f'{sample}-aot-map.xml').read_text()
    checks={name:(name in text) for name in ('AudioSession','PhysicsWorld','BoundUiSession','AuthoredScene','MissionGame','RoomGame')}
    aot[sample]=checks
    assert not checks['AudioSession'] and not checks['PhysicsWorld'] and not checks['MissionGame'] and not checks['RoomGame'],(sample,checks)
    if sample!='ui':assert not checks['BoundUiSession'],(sample,checks)
# Confirm the independent projects and restore graphs have no source/project references.
for sample in ('empty','sprite','ui'):
    project=(proof/'consumers'/sample/'Sample.csproj').read_text()
    assert 'ProjectReference' not in project and 'Compile Include' not in project
    assets=json.loads((proof/'consumers'/sample/'obj/project.assets.json').read_text())
    assert not any(v.get('type')=='project' for v in assets['libraries'].values())
captures=[(proof/'logs'/f'ui-{mode}.bmp').read_bytes() for mode in ('fdd','trim','aot')]
assert captures[0]==captures[1]==captures[2],'UI captures differ between runtime modes'
packages={p.name:dict(bytes=p.stat().st_size,sha256=hashlib.sha256(p.read_bytes()).hexdigest()) for p in (proof/'feed').glob('Dotnet2D*.nupkg')}
report=dict(packages=packages,outputs=rows,aot_engine_roots=aot,managed_types=records)
(proof/'measurements.json').write_text(json.dumps(report,indent=2)+'\n')
for row in rows: print(json.dumps(row))
print('PACKAGE CONTENT/ROOT/DEPENDENCY ASSERTIONS PASS')
