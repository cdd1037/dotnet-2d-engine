using System.Text.Json;
namespace GameAuthoringLab;

internal static class DiagnosticsDemo
{
    internal static int Run(bool headless,int frames,bool scenario)
    {
        if(scenario&&frames==0)frames=3;
        using var engine=EngineHost.Create(headless,64);
        using var white=engine.Textures.Acquire(new AssetRoot(),"regions.bmp");
        var debug=new DebugDrawBuffer(16,new(white.Handle,new TextureRegion(7,7,1,1))){Enabled=true};
        var timings=new CpuTimings{Enabled=true};var logs=new DiagnosticLog(16){MinimumLevel=DiagnosticLevel.Info};
        var actions=new InputActionMap(InputBinding.Key(1,PhysicalKey.D),InputBinding.Key(2,PhysicalKey.T),InputBinding.Key(4,PhysicalKey.Escape));
        var camera=new Camera{Zoom=1};
        SpriteDrawV2[] scene=[SpriteDrawV2.Create(new(){M11=1,M22=1,X=128,Y=128,Width=160,Height=96,R=.25f,G=.35f,B=.7f,A=1,Texture=white.Handle},white.Handle==0?null:new TextureRegion(7,7,1,1))];
        string? capture=Environment.GetEnvironmentVariable("GAL_DIAGNOSTICS_CAPTURE_DIR");
        if(!headless&&capture is not null)Directory.CreateDirectory(capture);
        logs.TryWrite(DiagnosticLevel.Info,100,"demo.start","D toggles debug draw; T toggles CPU timing; Escape exits");
        int rendered=0;
        while(frames==0||rendered<frames)
        {
            timings.BeginFrame();bool toggleTiming=false;
            try
            {
                InputSnapshot input;ActionState action;
                using(timings.Measure(CpuPhase.Input)){input=engine.PollInput();action=actions.Update(input);}
                if(input.Quit!=0||(action.Pressed&4)!=0){timings.AbortFrame();break;}
                using(timings.Measure(CpuPhase.Update))
                {
                    if(scenario)debug.Enabled=rendered!=1;
                    else if((action.Pressed&1)!=0){debug.Enabled=!debug.Enabled;logs.TryWrite(DiagnosticLevel.Info,101,"debug.enabled","debug visibility changed",rendered,debug.Enabled?1:0);}
                    toggleTiming=(action.Pressed&2)!=0;
                }
                using(timings.Measure(CpuPhase.Extraction))
                {
                    debug.Clear();
                    debug.TryAddRectangle(128,128,160,96,4,0,1,1);
                    debug.TryAddLine(48,300,350,300,8,1,1,0);
                    debug.TryAddLine(400,120,520,240,6,1,0,1);
                }
                using(timings.Measure(CpuPhase.Render))
                {
                    if(!headless&&capture is not null&&rendered<3)Native.Check(UiNative.Capture(engine.NativeContext,Path.GetFullPath(Path.Combine(capture,$"frame-{rendered}.bmp"))),"diagnostic capture");
                    engine.DrawWithOverlay(camera,scene,debug.RegionDraws);
                }
                timings.EndFrame();
            }
            catch{timings.AbortFrame();throw;}
            if(toggleTiming){timings.Enabled=!timings.Enabled;logs.TryWrite(DiagnosticLevel.Info,102,"timing.enabled","CPU timing changed",rendered,timings.Enabled?1:0);}
            rendered++;
            if(!scenario&&!headless)Thread.Sleep(1);
        }
        var stats=engine.GetStats();logs.TryWrite(DiagnosticLevel.Info,103,"demo.stop","completed frames",rendered,stats.Frames);
        // Explicit output at the boundary; no reflection, serialization registry or hot-loop formatting.
        using var output=Console.OpenStandardOutput();using var json=new Utf8JsonWriter(output,new(){Indented=true});
        json.WriteStartObject();json.WriteString("kind","diagnostics");json.WriteBoolean("headless",headless);json.WriteNumber("frames",stats.Frames);json.WriteNumber("submittedQuads",stats.Sprites);json.WriteNumber("worldDrawCalls",stats.DrawCalls);
        json.WriteNumber("debugDropped",debug.DroppedPrimitiveCount);json.WriteNumber("logsDropped",logs.Dropped);json.WriteNumber("cpuFrames",timings.Frames.Count);json.WriteNumber("cpuFrameAverageSeconds",timings.Frames.AverageSeconds);
        json.WriteStartArray("cpuPhases");for(var phase=CpuPhase.Input;phase<=CpuPhase.Other;phase++)
        {var sample=timings.GetPhase(phase);json.WriteStartObject();json.WriteString("phase",phase.ToString());json.WriteNumber("count",sample.Count);json.WriteNumber("averageSeconds",sample.AverageSeconds);json.WriteEndObject();}json.WriteEndArray();
        json.WriteStartArray("logs");while(logs.TryRead(out var entry))
        {json.WriteStartObject();json.WriteNumber("sequence",entry.Sequence);json.WriteString("level",entry.Level.ToString());json.WriteNumber("code",entry.Code);json.WriteString("category",entry.Category);json.WriteString("message",entry.Message);json.WriteNumber("frame",entry.Frame);json.WriteNumber("value",entry.Value);json.WriteEndObject();}json.WriteEndArray();json.WriteEndObject();json.Flush();
        if(scenario&&(stats.Frames!=(uint)frames||timings.Frames.Count!=(ulong)frames||logs.Dropped!=0||debug.DroppedPrimitiveCount!=0))throw new InvalidOperationException("Diagnostic scenario count mismatch.");
        return 0;
    }
}
