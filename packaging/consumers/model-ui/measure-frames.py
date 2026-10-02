#!/usr/bin/env python3
"""Supplementary same-frame-work JIT comparison; baseline inputs are read-only."""
import json,os,shutil,statistics,subprocess,sys,time
from pathlib import Path
current,baseline,out,dotnet=map(lambda value:Path(value).resolve(),sys.argv[1:])
assert not out.exists(),'Select a fresh output directory'
out.mkdir(parents=True)
program='''using System.Diagnostics;
using System.Globalization;
using GameAuthoringLab;
CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;
var assets=new AssetRoot(Path.Combine(AppContext.BaseDirectory,"assets"));
using var engine=EngineHost.Create(maxSprites:32);
using var ui=SESSION;
ui.LoadAsset(assets,"ui/minimal.rml");
void Draw()=>engine.Draw(new Camera{Zoom=1},ReadOnlySpan<Sprite>.Empty);
Draw();var a=new Model("Value A");var b=new Model("Value B");
ui.Apply(a);Draw();
const int warmup=10,iterations=100;
for(int i=0;i<warmup;i++){ui.Apply((i&1)==0?b:a);Draw();}
long allocation=GC.GetAllocatedBytesForCurrentThread();var before=Stopwatch.GetTimestamp();
for(int i=0;i<iterations;i++){if(!ui.Apply((i&1)==0?b:a))throw new Exception("Missing update");Draw();}
var elapsed=Stopwatch.GetElapsedTime(before).TotalMicroseconds/iterations;
long allocated=GC.GetAllocatedBytesForCurrentThread()-allocation;
Console.WriteLine($"{{\\"changed_apply_draw_us\\":{elapsed:F6},\\"calling_thread_alloc_bytes\\":{allocated},\\"iterations\\":{iterations}}}");
sealed record Model(string Text);
'''
base_env=os.environ.copy()
for key in ('GAL_ASSET_ROOT','GAL_MODEL_UI_CAPTURE_DIR','LD_PRELOAD','LD_AUDIT'):
    base_env.pop(key,None)
base_env.update(SDL_VIDEODRIVER='offscreen',SDL_AUDIODRIVER='dummy',VK_ICD_FILENAMES=str(current/'.deps/graphics-sysroot/usr/share/vulkan/icd.d/lvp_icd.json'),LD_LIBRARY_PATH=str(current/'.deps/graphics-sysroot/usr/lib/x86_64-linux-gnu'),GAL_UI_FONT='/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc',DOTNET_TieredCompilation='0',DOTNET_CLI_TELEMETRY_OPTOUT='1',DOTNET_GENERATE_ASPNET_CERTIFICATE='false',DOTNET_SKIP_FIRST_TIME_EXPERIENCE='true')
results={'configuration':'Framework-dependent JIT, tiering off, existing SDK 10.0.401; 10 warmup + 100 measured alternating Apply+Draw; no render in original Apply-only test','limitations':'Draw return does not establish GPU completion or monitor presentation. Shared-host lavapipe; per-thread managed allocation only; native allocations unmeasured. Equivalent completed frame submission, with backend DOM/model update placement still different.','runs':{},'medians':{}}
prepared=[]
for label,root,session in [('baseline',baseline,'new BoundUiSession<Model>(engine,new UiBindings<Model>().Text("caption",m=>m.Text))'),('generic',current,'new UiModelSession<Model>(engine,new UiRecord<Model>().Text("text",m=>m.Text))')]:
    p=out/label;p.mkdir();(p/'feed').mkdir()
    source=baseline/'measurement/consumer' if label=='baseline' else current/'packaging/consumers/model-ui/benchmark'
    shutil.copytree(source/'assets',p/'consumer/assets');shutil.copyfile(source/'Sample.csproj',p/'consumer/Sample.csproj')
    (p/'consumer/Program.cs').write_text(program.replace('SESSION',session))
    for package in (root/'build-packages/feed').glob('Dotnet2D*.nupkg'):shutil.copyfile(package,p/'feed'/package.name)
    (p/'NuGet.Config').write_text('<configuration><packageSources><clear/><add key="local" value="'+str(p/'feed')+'"/></packageSources></configuration>')
    env=base_env|{'DOTNET_CLI_HOME':str(p/'dotnet-home'),'NUGET_PACKAGES':str(p/'nuget-cache'),'NUGET_HTTP_CACHE_PATH':str(p/'http-cache'),'MESA_SHADER_CACHE_DIR':str(p/'mesa-cache')}
    project=p/'consumer/Sample.csproj'
    for name,args in [('restore',['restore',str(project),'--configfile',str(p/'NuGet.Config')]),('publish',['publish',str(project),'-c','Release','--no-restore','-m:1','-nr:false','-p:UseSharedCompilation=false','-o',str(p/'publish')])]:
        proc=subprocess.run([str(dotnet),*args],env=env,text=True,capture_output=True)
        (p/f'{name}.log').write_text(proc.stdout+proc.stderr);proc.check_returncode()
    cmd=[str(dotnet),str(p/'publish/UiBridge.Benchmark.dll')]
    prepared.append((label,p,env,cmd));results['runs'][label]=[]
for i in range(6):
    for label,p,env,cmd in prepared:
        proc=subprocess.run(cmd,cwd=p,env=env,text=True,capture_output=True);(p/f'run-{i}.log').write_text(proc.stdout+proc.stderr);proc.check_returncode()
        record=json.loads(next(line for line in proc.stdout.splitlines() if line.startswith('{')));record['index']=i;results['runs'][label].append(record);print(label,i,record,flush=True)
for label,runs in results['runs'].items():
    results['medians'][label]={k:statistics.median(r[k] for r in runs[1:]) for k in runs[0] if k!='index'}
(out/'measurements.json').write_text(json.dumps(results,indent=2)+'\n')
print(json.dumps(results['medians'],indent=2))
