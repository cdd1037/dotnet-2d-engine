#!/usr/bin/env python3
"""Matching minimal UI timings and package integrity from independent publishes."""
import hashlib,json,os,statistics,subprocess,sys,time
from pathlib import Path
from zipfile import ZipFile
root,proof,dotnet=map(lambda value:Path(value).resolve(),sys.argv[1:])
digest=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
env=os.environ.copy()
for key in ('GAL_ASSET_ROOT','GAL_MODEL_UI_CAPTURE_DIR','LD_PRELOAD','LD_AUDIT'):
    env.pop(key,None)
env.update(SDL_VIDEODRIVER='offscreen',SDL_AUDIODRIVER='dummy',VK_ICD_FILENAMES=str(root/'.deps/graphics-sysroot/usr/share/vulkan/icd.d/lvp_icd.json'),LD_LIBRARY_PATH=str(root/'.deps/graphics-sysroot/usr/lib/x86_64-linux-gnu'),GAL_UI_FONT=env.get('GAL_UI_FONT','/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc'),DOTNET_TieredCompilation='0',DOTNET_CLI_TELEMETRY_OPTOUT='1')
report={'source_commit':subprocess.check_output(['git','-C',str(root),'rev-parse','HEAD'],text=True).strip(),'host':subprocess.check_output(['uname','-a'],text=True).strip(),'sdk':subprocess.check_output([str(dotnet),'--version'],text=True).strip(),'runtime_framework_version':'10.0.12','font_sha256':digest(Path(env['GAL_UI_FONT'])),'configuration':{'native':'Release; RmlUi,mixer,physics ON; SVG OFF; DT_RPATH=$ORIGIN','JIT':'framework-dependent Release publish; installed runtime excluded from bytes; DOTNET_TieredCompilation=0','AOT':'Release linux-x64; PublishAot=true StripSymbols=true; runtime 10.0.12','scenario':'one text field in generic root state, body data-model=model; same two 7-byte ASCII strings, 2000 warmup + 20000 measured calls as baseline','limitations':'Fresh Mesa cache is shader-cold, not OS-cache cold. First-content ends at Draw return, not GPU completion or monitor presentation. Timed Apply loops contain no Draw/layout/render. Generic Apply copies/validates CPU snapshots and marks model dirty; RmlUi binding/layout updates are deferred to subsequent Draw. Baseline BoundUi mutates a direct text target during Apply, so changed Apply timings do not represent identical completed DOM work. Calling-thread managed allocations exclude native allocations/process memory. Shared-host lavapipe observations, not budgets.'},'packages':{},'outputs':{},'runs':{},'medians':{},'verification':{}}
for path in (proof/'feed').glob('Dotnet2D*.nupkg'):
    report['packages'][path.name]={'bytes':path.stat().st_size,'sha256':digest(path)}
native_package=next((proof/'feed').glob('Dotnet2D.Native.*.nupkg'))
engine_package=next((proof/'feed').glob('Dotnet2D.Engine.*.nupkg'))
with ZipFile(native_package) as z:
    payload={Path(n).name:hashlib.sha256(z.read(n)).hexdigest() for n in z.namelist() if n.startswith('runtimes/linux-x64/native/') and not n.endswith('/')}
    assert len(payload)==8,payload
    manifest=json.loads(z.read('manifest.json'))
with ZipFile(engine_package) as z:
    dll=next(n for n in z.namelist() if n.endswith('/Dotnet2D.Engine.dll'))
    engine_hash=hashlib.sha256(z.read(dll)).hexdigest()
for sample in ('benchmark','consumer'):
    project=(proof/sample/'Sample.csproj').read_text()
    assert '<ProjectReference' not in project and '<PackageReference' in project
    for mode in ('jit','aot'):
        out=proof/'publish'/f'{sample}-{mode}';files=[f for f in out.rglob('*') if f.is_file()]
        assert {f.name:digest(f) for f in files if f.name in payload}==payload
        if mode=='jit':assert digest(out/'Dotnet2D.Engine.dll')==engine_hash
        group=lambda condition:sum(f.stat().st_size for f in files if condition(f))
        report['outputs'][f'{sample}-{mode}']={'distribution_without_symbols_bytes':group(lambda f:f.suffix not in ('.pdb','.dbg')),'symbols_bytes':group(lambda f:f.suffix in ('.pdb','.dbg')),'native_payload_bytes':group(lambda f:f.name in payload),'engine_managed_bytes':group(lambda f:f.name=='Dotnet2D.Engine.dll'),'application_bytes':group(lambda f:f.name in ('UiBridge.Benchmark','UiBridge.Benchmark.dll','Sample.model-ui','Sample.model-ui.dll')),'asset_bytes':group(lambda f:'assets' in f.relative_to(out).parts),'notice_bytes':group(lambda f:'licenses' in f.relative_to(out).parts),'metadata_bytes':group(lambda f:f.name.endswith(('.deps.json','.runtimeconfig.json'))),'files':{str(f.relative_to(out)):{'bytes':f.stat().st_size,'sha256':digest(f)} for f in files}}
for mode in ('jit','aot'):
    out=proof/'publish'/f'benchmark-{mode}'
    cmd=[str(dotnet),str(out/'UiBridge.Benchmark.dll')] if mode=='jit' else [str(out/'UiBridge.Benchmark')]
    cache=proof/f'mesa-cache-benchmark-{mode}';assert not cache.exists(),f'Choose fresh cache path: {cache}'
    env['MESA_SHADER_CACHE_DIR']=str(cache);runs=[]
    for i in range(6):
        begin=time.perf_counter();proc=subprocess.run(cmd,cwd=proof,env=env,text=True,capture_output=True,check=True);elapsed=(time.perf_counter()-begin)*1000
        (proof/'logs'/f'benchmark-{mode}-run-{i}.log').write_text(proc.stdout+proc.stderr)
        record=json.loads(next(line for line in proc.stdout.splitlines() if line.startswith('{')))
        assert record['unchanged_native_calls']==record['iterations'] and record['unchanged_native_apply_calls']==0
        assert record['changed_native_calls']==2*record['iterations'] and record['changed_native_apply_calls']==record['iterations']
        assert record['unchanged_alloc_bytes']==record['changed_alloc_bytes']==0
        record.update(process_ms=elapsed,shader_cache='cold' if i==0 else 'warm',index=i)
        runs.append(record);print(mode,i,json.dumps(record),flush=True)
    probe_env=env.copy();probe_env['LD_DEBUG']='libs'
    probe=subprocess.run(cmd,cwd=proof,env=probe_env,text=True,capture_output=True,check=True)
    (proof/'logs'/f'benchmark-{mode}-loader.log').write_text(probe.stdout+probe.stderr)
    loaded=[line.split('calling init:',1)[1].strip() for line in probe.stderr.splitlines() if 'calling init:' in line and line.rstrip().endswith('/libgal.so')]
    assert len(loaded)==1 and Path(loaded[0]).is_relative_to(out),(out,loaded)
    report['runs'][mode]=runs
    report['medians'][mode]={k:statistics.median(r[k] for r in runs[1:]) for k in runs[0] if isinstance(runs[0][k],(int,float)) and k!='index'}
for mode in ('jit','aot'):
    log=(proof/'logs'/f'consumer-{mode}-run.log').read_text()
    assert 'PACKAGE MODEL UI PASS fixtures=3' in log and 'NATIVE_LOADED '+str(proof/'publish'/f'consumer-{mode}')+'/' in log
    assert 'UI MODEL PIXELS PASS assertions=21' in (proof/'logs'/f'consumer-{mode}-pixels.log').read_text()
    for name in ('inventory','dialogue','settings'):
        capture=proof/'captures'/mode/f'{name}.bmp';assert capture.is_file() and capture.stat().st_size>100000
report['verification']={'consumer_package_references_only':True,'fresh_local_only_nuget_cache':str(proof/'nuget-cache'),'jit_engine_matches_package':True,'all_eight_dsos_in_all_four_publishes_match_package':True,'loaded_native_is_inside_each_publish':True,'benchmark_runs_passed':12,'untimed_loader_verification_runs_passed':2,'rich_consumer_runs_passed':2,'rich_fixture_captures':6,'rich_pixel_checks':42,'rich_pixel_logs':['logs/consumer-jit-pixels.log','logs/consumer-aot-pixels.log'],'rich_event_validation':'Independent public SDL ABI injection validates actual native exact-key click packets, application dispatch, stale revisions, pointer-down/reorder/up invalidation, a fresh click after reorder, four accepted continuous text commits without refocus, and an unaccepted draft preserved across an unrelated scalar update in both JIT and NativeAOT. Further public constructed packets exercise dialogue and nested-settings handlers.','native_libgal_bytes':next(f['bytes'] for f in manifest['files'] if f['path'].endswith('/libgal.so')),'rmlui_static_archive_sha256':digest(root/'.deps/rmlui-build/librmlui.a')}
(proof/'measurements.json').write_text(json.dumps(report,indent=2)+'\n')
print(json.dumps({k:report[k] for k in ('packages','medians','verification')},indent=2))
