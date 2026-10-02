#!/usr/bin/env python3
"""Measure produced files; do not equate native package selection with native function trimming."""
import json, hashlib, subprocess, sys
import xml.etree.ElementTree as ET
from pathlib import Path
from zipfile import ZipFile
proof=Path(sys.argv[1]).resolve()
publish=Path(sys.argv[2]).resolve() if len(sys.argv)>2 else proof/'publish'
native_package=next((proof/'feed').glob('Dotnet2D.Native.Linux.x64.*.nupkg'))
with ZipFile(native_package) as package:
    native_entries=[n for n in package.namelist() if n.startswith('runtimes/linux-x64/native/') and not n.endswith('/')]
    expected_native={Path(n).name:hashlib.sha256(package.read(n)).hexdigest() for n in native_entries}
    expected_native_bytes=sum(package.getinfo(n).file_size for n in native_entries)
rows=[]
for sample in ('empty','sprite','ui'):
    for mode in ('fdd','trim','aot'):
        directory=publish/f'{sample}-{mode}'
        files=[p for p in directory.rglob('*') if p.is_file()]
        app=[p for p in files if p.name in (f'Sample.{sample}',f'Sample.{sample}.dll')]
        native=[p for p in files if p.name.startswith(('libgal.so','libSDL3.so','libfreetype.so','libpng16.so','libz.so','libbz2.so','libbrotlidec.so','libbrotlicommon.so'))]
        assets=[p for p in files if 'assets' in p.relative_to(directory).parts]
        notices=[p for p in files if 'licenses' in p.relative_to(directory).parts]
        symbols=[p for p in files if p.suffix in ('.pdb','.dbg')]
        managed=[p for p in files if p.name=='Dotnet2D.Engine.dll']
        metadata=[p for p in files if p.name.endswith(('.deps.json','.runtimeconfig.json'))]
        assigned=set(app+native+assets+notices+symbols+managed+metadata)
        other=[p for p in files if p not in assigned]
        total=lambda group:sum(p.stat().st_size for p in group)
        rows.append(dict(sample=sample,mode=mode,app_bytes=total(app),engine_managed_bytes=total(managed),native_payload_bytes=total(native),asset_bytes=total(assets),notice_bytes=total(notices),metadata_bytes=total(metadata),symbols_bytes=total(symbols),framework_other_bytes=total(other),distribution_without_symbols_bytes=total(files)-total(symbols),native_files=len(native)))
        expected_notices=1 if sample=='empty' else 2
        assert len(notices)==expected_notices,(sample,mode,'missing redistribution notices')
        if sample=='empty': assert not native and not assets,(sample,mode,'unexpected native/assets')
        else:
            assert len(native)==len(expected_native) and total(native)==expected_native_bytes,(sample,mode,'native closure changed',total(native))
            assert all(hashlib.sha256(p.read_bytes()).hexdigest()==expected_native[p.name] for p in native),(sample,mode,'native asset changed')
        if sample=='sprite': assert len(assets)==2,(sample,mode,'sourcegen fixture files missing')
        if sample=='ui': assert len(assets)==2,(sample,mode,'UI source files missing')
with ZipFile(next((proof/'feed').glob('Dotnet2D.Engine.*.nupkg'))) as package:
    managed_notice=package.read('LICENSE')
with ZipFile(native_package) as package:
    native_notice=package.read('LICENSE.txt')
for sample in ('empty','sprite','ui'):
    for mode in ('fdd','trim','aot'):
        directory=publish/f'{sample}-{mode}'
        assert (directory/'licenses/Dotnet2D.Engine/LICENSE.txt').read_bytes()==managed_notice
        if sample!='empty':assert (directory/'licenses/Dotnet2D.Native.Linux.x64/LICENSE.txt').read_bytes()==native_notice
records=[json.loads(line) for line in (proof/'logs/managed-types.jsonl').read_text().splitlines() if line.startswith('{')]
assert len(records)==4
assert not records[0]['exists'],'unused engine assembly should trim entirely'
unused_modules=('AudioSession','PhysicsWorld','FrameClip','FramePlayer','Tween','TimingScope','EngineTimer',
                'TileMap','TileMapInstance','TileMapCollision',
                'DiagnosticLog','CpuTimings','DebugDrawBuffer','MaterialAsset','MaterialJsonContext',
                'RenderTargetStore','RenderTarget','TargetNative')
sample_only=('MissionGame','RoomGame','Program','SelfTests','TileMovementClock','TileMovementLevel','TileMovementDemo')
full_types=set(records[3]['types'])
assert records[3]['exists'],'untrimmed package assembly missing'
for name in unused_modules:
    assert 'GameAuthoringLab.'+name in full_types,('untrimmed package lacks positive control',name)
for name in sample_only:
    assert 'GameAuthoringLab.'+name not in full_types,('package includes sample-only code',name)
with ZipFile(next((proof/'feed').glob('Dotnet2D.Engine.*.nupkg'))) as package:
    assert package.read('lib/net10.0/Dotnet2D.Engine.dll')==(publish/'empty-fdd/Dotnet2D.Engine.dll').read_bytes(),'full assembly differs from measured package'
for record in records[1:3]:
    types=set(record['types'])
    for name in unused_modules+sample_only:
        forbidden='GameAuthoringLab.'+name
        assert not any(t==forbidden or t.startswith(forbidden+'`') for t in types),(record['path'],forbidden,'unexpected managed root')
assert not any('BoundUi' in t or 'UiAuthoring' in t for t in records[1]['types']),'sprite consumer retained UI'
assert any('UiModelSession' in t for t in records[2]['types']),'UI consumer lost model root'
assert not any('BoundUiSession' in t for t in records[2]['types']),'UI consumer retained superseded binding root'
# AOT symbol maps, unlike text search in stripped executables, identify compiled engine methods.
aot={}
for sample in ('empty','sprite','ui'):
    text=(proof/'logs'/f'{sample}-aot-map.xml').read_text()
    checks={name:('GameAuthoringLab_'+name in text or 'GameAuthoringLab.'+name in text) for name in unused_modules+sample_only+('BoundUiSession','UiModelSession','AuthoredScene')}
    aot[sample]=checks
    assert not any(checks[name] for name in unused_modules+sample_only),(sample,checks)
    if sample=='empty':assert 'Dotnet2D_Engine' not in text and 'Dotnet2D.Engine' not in text,'empty AOT contains an engine assembly node'
    assert not checks['BoundUiSession'],(sample,'retained superseded binding root')
    if sample!='ui':assert not checks['UiModelSession'],(sample,checks)
    if sample=='ui':assert checks['UiModelSession'],(sample,'missing expected UI AOT root')
    if sample=='sprite':assert checks['AuthoredScene'],(sample,'missing expected scene AOT root')
# These native exports remain in the complete prebuilt DSO even though the managed
# consumers do not root their corresponding modules. Hash checks above establish
# the same native payload in every nonempty output, including trimmed/AOT modes.
symbols=subprocess.check_output(['nm','-D','--defined-only',str(publish/'sprite-aot/libgal.so')],text=True)
native_exports={name:any(line.split()[-1].split('@')[0]==name for line in symbols.splitlines()) for name in
                ('gal_audio_open','gal_physics_open','gal_submit_draws_clipped_v1','gal_material_create_v1','gal_target_create_v1','gal_render_frame_v1')}
assert all(native_exports.values()),('full native profile lost expected exports',native_exports)
# Confirm the independent projects and restore graphs have no source/project references.
for sample in ('empty','sprite','ui'):
    project=(proof/'consumers'/sample/'Sample.csproj').read_text()
    assert 'ProjectReference' not in project and 'Compile Include' not in project
    assets=json.loads((proof/'consumers'/sample/'obj/project.assets.json').read_text())
    assert not any(v.get('type')=='project' for v in assets['libraries'].values())
captures=[(proof/'logs'/f'ui-{mode}.bmp').read_bytes() for mode in ('fdd','trim','aot')]
assert captures[0]==captures[1]==captures[2],'UI captures differ between runtime modes'
packages={p.name:dict(bytes=p.stat().st_size,sha256=hashlib.sha256(p.read_bytes()).hexdigest()) for p in (proof/'feed').glob('Dotnet2D*.nupkg')}
for name,info in packages.items():
    with ZipFile(proof/'feed'/name) as package:
        nuspec=ET.fromstring(package.read(next(n for n in package.namelist() if n.endswith('.nuspec'))))
        info['repository_commit']=nuspec.find('.//{*}repository').attrib['commit']
with ZipFile(native_package) as package:native_manifest=json.loads(package.read('manifest.json'))
commits={info['repository_commit'] for info in packages.values()}
assert len(commits)==1 and native_manifest['repository_commit'] in commits,'managed/native package source revisions differ'
report=dict(packages=packages,outputs=rows,aot_engine_roots=aot,managed_types=records,native_full_profile_exports=native_exports,native_profile=native_manifest['profile'],repository_commit=next(iter(commits)))
(proof/('measurements.json' if publish==proof/'publish' else 'measurements-notices.json')).write_text(json.dumps(report,indent=2)+'\n')
for row in rows: print(json.dumps(row))
print('PACKAGE CONTENT/ROOT/DEPENDENCY ASSERTIONS PASS')
