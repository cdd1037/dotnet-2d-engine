namespace GameAuthoringLab;
internal static class DiagnosticsTests
{
    public static int Run()
    {
        int n=0;void Check(bool ok,string label){if(!ok)throw new Exception("DIAGNOSTICS: "+label);n++;}
        void Reject<T>(Action call,string label)where T:Exception{try{call();}catch(T){n++;return;}throw new Exception("DIAGNOSTICS accepted: "+label);}
        int reads=0;long tick=0;
        var log=new DiagnosticLog(2,true,()=>{reads++;return ++tick;});
        Check(!log.TryWrite(DiagnosticLevel.Info,0,null!,null!)&&reads==0&&log.Count==0,"default off skips payload validation and clock");
        log.MinimumLevel=DiagnosticLevel.Warning;
        Check(!log.IsEnabled(DiagnosticLevel.Info)&&log.IsEnabled(DiagnosticLevel.Error),"level filter");
        Check(!log.TryWrite(DiagnosticLevel.Trace,1,"test","filtered")&&reads==0,"filtered log has no timestamp");
        Check(log.TryWrite(DiagnosticLevel.Warning,11,"asset.load","missing",7,42)&&log.TryWrite(DiagnosticLevel.Error,12,"draw","invalid",8,-3),"accepted structured entries");
        Check(!log.TryWrite(DiagnosticLevel.Error,13,"full","overflow")&&log.Dropped==1&&reads==2,"full queue counts dropped new entry without clock");
        Check(log.TryRead(out var entry)&&entry==new DiagnosticEntry(1,DiagnosticLevel.Warning,11,"asset.load","missing",7,42,1),"FIFO preserves copied scalar fields");
        Check(log.TryWrite(DiagnosticLevel.Error,14,"recover","accepted")&&log.Written==3,"drain permits ring wrap");
        Check(log.TryRead(out entry)&&entry.Code==12&&log.TryRead(out entry)&&entry.Sequence==3&&entry.Code==14,"ring wrap order");
        Check(!log.TryRead(out entry)&&entry==default,"empty drain clears output");
        Reject<ArgumentOutOfRangeException>(()=>log.MinimumLevel=(DiagnosticLevel)99,"invalid filter");
        Reject<ArgumentOutOfRangeException>(()=>log.TryWrite(DiagnosticLevel.Off,1,"test","message"),"invalid entry level");
        Reject<ArgumentException>(()=>log.TryWrite(DiagnosticLevel.Error,1,"bad/category","message"),"invalid category");
        Reject<ArgumentException>(()=>log.TryWrite(DiagnosticLevel.Error,1,new string('a',49),"message"),"category bound");
        Reject<ArgumentException>(()=>log.TryWrite(DiagnosticLevel.Error,1,"test",new string('a',1025)),"message bound");
        Reject<ArgumentOutOfRangeException>(()=>log.TryWrite(DiagnosticLevel.Error,0,"test","message"),"zero event code");
        Reject<ArgumentOutOfRangeException>(()=>log.TryWrite(DiagnosticLevel.Error,1,"test","message",-1),"negative frame");
        Reject<ArgumentOutOfRangeException>(()=>log.TryWrite(DiagnosticLevel.Error,1,"test","message",value:double.NaN),"nonfinite numeric field");
        Reject<ArgumentOutOfRangeException>(()=>new DiagnosticLog(0),"zero log capacity");
        Reject<ArgumentOutOfRangeException>(()=>new DiagnosticLog(65537),"maximum log capacity");
        log.TryWrite(DiagnosticLevel.Error,15,"test","clear");log.Clear();Check(log.Count==0&&log.Written==4&&log.Dropped==1,"clear releases queued data and keeps counters");
        var unstamped=new DiagnosticLog(1,false,()=>throw new Exception("Unrequested timestamp")){MinimumLevel=DiagnosticLevel.Info};
        Check(unstamped.TryWrite(DiagnosticLevel.Info,1,"test","no clock")&&unstamped.TryRead(out entry)&&entry.TimestampTicks==0,"timestamps explicitly opt in");
        Check(Task.Run(()=>{try{log.Clear();return false;}catch(InvalidOperationException){return true;}}).GetAwaiter().GetResult(),"log ownership thread");

        reads=0;tick=100;var timer=new CpuTimings(()=>{reads++;return tick;},100);
        timer.BeginFrame();using(timer.Measure((CpuPhase)99)){}timer.EndFrame();timer.AbortFrame();
        Check(reads==0&&timer.Frames.Count==0&&!timer.FrameActive,"disabled timing performs no clock reads");
        timer.Enabled=true;
        Reject<InvalidOperationException>(()=>timer.Measure(CpuPhase.Update),"phase outside frame");
        Reject<InvalidOperationException>(()=>timer.EndFrame(),"end outside frame");
        timer.BeginFrame();tick=110;var scope=timer.Measure(CpuPhase.Input);var copy=scope;
        Reject<InvalidOperationException>(()=>timer.BeginFrame(),"nested frame");
        Reject<InvalidOperationException>(()=>timer.Measure(CpuPhase.Update),"nested phase");
        Reject<InvalidOperationException>(()=>timer.EndFrame(),"end while phase active");
        Reject<InvalidOperationException>(()=>timer.Enabled=false,"disable during frame");
        Reject<InvalidOperationException>(()=>timer.Reset(),"reset during frame");
        tick=120;scope.Dispose();copy.Dispose();tick=125;var next=timer.Measure(CpuPhase.Update);copy.Dispose();tick=145;next.Dispose();tick=150;timer.EndFrame();
        Check(timer.Frames==new CpuTimingSample(1,.5,.5,.5,.5)&&timer.GetPhase(CpuPhase.Input).LastSeconds==.1&&timer.GetPhase(CpuPhase.Update).LastSeconds==.2,"exact injected CPU durations; copied scope cannot finish later phase");
        timer.BeginFrame();tick=160;timer.EndFrame();
        Check(timer.Frames.Count==2&&timer.Frames.MinimumSeconds==.1&&timer.Frames.MaximumSeconds==.5&&timer.Frames.AverageSeconds==.3,"frame aggregate boundaries");
        timer.BeginFrame();var stale=timer.Measure(CpuPhase.Other);timer.AbortFrame();timer.AbortFrame();
        Check(timer.AbortedFrames==1&&timer.GetPhase(CpuPhase.Other).Count==0&&!timer.FrameActive,"abort drops unfinished phase/frame");
        timer.Reset();timer.BeginFrame();next=timer.Measure(CpuPhase.Other);stale.Dispose();tick=165;next.Dispose();timer.EndFrame();
        Check(timer.Frames.Count==1&&timer.AbortedFrames==0&&timer.GetPhase(CpuPhase.Other).LastSeconds==.05,"reset does not revive old tickets");
        Reject<ArgumentOutOfRangeException>(()=>timer.GetPhase((CpuPhase)99),"invalid sample phase");
        timer.BeginFrame();Reject<ArgumentOutOfRangeException>(()=>timer.Measure((CpuPhase)99),"invalid phase");tick=164;
        Reject<InvalidOperationException>(()=>timer.EndFrame(),"clock regression");timer.AbortFrame();
        tick=long.MaxValue-1;timer.BeginFrame();tick++;timer.EndFrame();
        Check(timer.Frames.LastSeconds==.01,"large tick baseline preserves one tick precision");
        Check(Task.Run(()=>{try{timer.Reset();return false;}catch(InvalidOperationException){return true;}}).GetAwaiter().GetResult(),"timing ownership thread");
        timer.Enabled=false;int beforeReads=reads;timer.BeginFrame();using(timer.Measure(CpuPhase.Input)){}timer.EndFrame();Check(reads==beforeReads,"disable stops timestamp reads");

        var reusable=new DiagnosticLog(1){MinimumLevel=DiagnosticLevel.Info};var liveTimer=new CpuTimings{Enabled=true};
        void One(){reusable.TryWrite(DiagnosticLevel.Info,1,"frame","ready",value:2);reusable.TryRead(out _);liveTimer.BeginFrame();using(liveTimer.Measure(CpuPhase.Update)){}liveTimer.EndFrame();}
        for(int i=0;i<128;i++)One();long before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<1000;i++)One();
        Check(GC.GetAllocatedBytesForCurrentThread()==before,"warmed logs, phase scopes and frames allocate no managed bytes");
        using var engine=EngineHost.Create(true,2);var camera=new Camera{Zoom=1};
        SpriteDrawV2[] scene=[SpriteDrawV2.Create(new(){M11=1,M22=1,Width=16,Height=16,R=1,G=1,B=1,A=1})];
        engine.DrawWithOverlay(camera,scene,scene);Check(engine.GetStats().Frames==1&&engine.GetStats().Sprites==2,"scene plus overlay is one frame");
        Reject<InvalidOperationException>(()=>engine.DrawWithOverlay(camera,scene,new[]{scene[0],scene[0]}),"combined capacity rejected");
        var invalid=scene[0];invalid.Version=99;
        Reject<InvalidOperationException>(()=>engine.DrawWithOverlay(camera,scene,new[]{invalid}),"invalid overlay rejects frame");
        engine.DrawWithOverlay(camera,scene,ReadOnlySpan<SpriteDrawV2>.Empty);Check(engine.GetStats().Frames==2,"rejected overlay recovers for next frame");
        Console.WriteLine($"DIAGNOSTICS PASS assertions={n}");return n;
    }
}
