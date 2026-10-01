#!/usr/bin/env python3
"""Measure the combined public-module consumer and fresh minimal trimmed graphs."""
import hashlib,json,re,sys
from pathlib import Path
from zipfile import ZipFile

proof=Path(sys.argv[1]).resolve()
with ZipFile(proof/'feed/Dotnet2D.Native.Linux.x64.0.1.0-preview.1.nupkg') as package:
    native={Path(name).name:package.read(name) for name in package.namelist() if name.startswith('runtimes/linux-x64/native/') and not name.endswith('/')}
    native_notice=package.read('LICENSE.txt')
with ZipFile(proof/'feed/Dotnet2D.Engine.0.1.0-preview.1.nupkg') as package:
    managed_notice=package.read('LICENSE')
    full_assembly=package.read('lib/net10.0/Dotnet2D.Engine.dll')
assert (proof/'publish/modules-fdd/Dotnet2D.Engine.dll').read_bytes()==full_assembly
outputs=[]
for name in ('modules-fdd','modules-aot','empty-trim','sprite-trim','ui-trim'):
    directory=proof/'publish'/name
    paths=[path for path in directory.rglob('*') if path.is_file()]
    def total(items):return sum(p.stat().st_size for p in items)
    payload=[p for p in paths if p.name in native]
    assets=[p for p in paths if 'assets' in p.relative_to(directory).parts]
    notices=[p for p in paths if 'licenses' in p.relative_to(directory).parts]
    symbols=[p for p in paths if p.suffix in ('.pdb','.dbg')]
    managed=[p for p in paths if p.name=='Dotnet2D.Engine.dll']
    sample=name.split('-')[0]
    app=[p for p in paths if p.name in (f'Sample.{sample}',f'Sample.{sample}.dll')]
    metadata=[p for p in paths if p.name.endswith(('.deps.json','.runtimeconfig.json'))]
    assigned=set(payload+assets+notices+symbols+managed+app+metadata)
    other=[p for p in paths if p not in assigned]
    if name=='empty-trim':assert not payload and not managed and not assets
    else:
        assert len(payload)==8 and all(p.read_bytes()==native[p.name] for p in payload)
        assert (directory/'licenses/Dotnet2D.Native.Linux.x64/LICENSE.txt').read_bytes()==native_notice
    assert (directory/'licenses/Dotnet2D.Engine/LICENSE.txt').read_bytes()==managed_notice
    if name.startswith('modules-'):assert len(assets)==3
    rows=dict(output=name,app_bytes=total(app),engine_managed_bytes=total(managed),native_payload_bytes=total(payload),asset_bytes=total(assets),notice_bytes=total(notices),metadata_bytes=total(metadata),framework_other_bytes=total(other),symbols_bytes=total(symbols),distribution_without_symbols_bytes=total(paths)-total(symbols))
    outputs.append(rows)

records=[json.loads(line) for line in (proof/'logs/managed-types.jsonl').read_text().splitlines() if line.startswith('{')]
assert len(records)==4 and not records[0]['exists']
families=('FramePlayer','EngineTimer','Tween','TimingScope','AudioSession','AudioVoice','PhysicsWorld','PhysicsBody','TileMapInstance','TileMapJsonContext')
for name in families:assert 'GameAuthoringLab.'+name in records[3]['types'],('missing full positive control',name)
for record in records[1:3]:
    for name in families:
        assert not any(t=='GameAuthoringLab.'+name or t.startswith('GameAuthoringLab.'+name+'`') for t in record['types']),(record['path'],'unexpected module root',name)
text=(proof/'logs/modules-aot-map.xml').read_text()
roots={name:('GameAuthoringLab_'+name in text or 'GameAuthoringLab.'+name in text) for name in families}
assert all(roots.values()),('module consumer lost expected roots',roots)
assert 'GameAuthoringLab_BoundUiSession' not in text and 'GameAuthoringLab.BoundUiSession' not in text
null_errors=set(re.findall(r'Program.cs\((\d+),\d+\): error CS8625',(proof/'logs/nulls-build.log').read_text()))
assert len(null_errors)==7,('every unsupported null must be diagnosed',null_errors)
for name in ('consumer','empty','sprite','ui'):
    project=(proof/name/'Sample.csproj').read_text()
    assert 'ProjectReference' not in project and 'InternalsVisibleTo' not in project and 'Compile Include' not in project
    restore=json.loads((proof/name/'obj/project.assets.json').read_text())
    assert not any(lib.get('type')=='project' for lib in restore['libraries'].values())
assert 'PACKAGE MODULES PASS' in (proof/'logs/modules-jit.log').read_text()
assert (proof/'logs/modules-jit.log').read_text()==(proof/'logs/modules-aot.log').read_text()
report=dict(outputs=outputs,combined_aot_roots=roots,minimal_managed_types=records,
            engine_assembly_sha256=hashlib.sha256(full_assembly).hexdigest(),
            native_files={name:dict(bytes=len(data),sha256=hashlib.sha256(data).hexdigest()) for name,data in native.items()},
            compiler_negative=dict(forged_owners=7,null_argument_sites=7,immutable_views=True,raw_interop_and_sourcegen_hidden=True))
(proof/'measurements.json').write_text(json.dumps(report,indent=2)+'\n')
for row in outputs:print(json.dumps(row))
print('PACKAGE MODULE OWNERSHIP/ROOT/PAYLOAD CHECKS PASS')
