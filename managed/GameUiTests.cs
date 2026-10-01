using System.Text;
namespace GameAuthoringLab;
internal static unsafe class GameUiTests
{
    public static int RunAuthoring()
    {
        int count=0;void Check(bool ok,string label){if(!ok)throw new Exception("GAME UI: "+label);count++;}
        var source=GameUiAuthoring.ValidateFiles(GameUiSession.SourcePath);Check(source.Rml.Contains("game-start",StringComparison.Ordinal),"fixed game profile accepted");
        void Reject(string rml,string css)
        {
            try{GameUiAuthoring.Validate(Encoding.UTF8.GetBytes(rml),Encoding.UTF8.GetBytes(css));}catch(UiAuthoringException){count++;return;}
            throw new Exception("Invalid game profile accepted");
        }
        Reject(source.Rml.Replace("game-start","unknown",StringComparison.Ordinal),source.Rcss);
        Reject(source.Rml.Replace("game-start","game-resume",StringComparison.Ordinal),source.Rcss);
        Reject(source.Rml.Replace("id=\"game-start\"","id=\"game-start\" onclick=\"x\"",StringComparison.Ordinal),source.Rcss);
        Reject(source.Rml.Replace("game.rcss","https://example.invalid/x.rcss",StringComparison.Ordinal),source.Rcss);
        Reject(source.Rml.Replace("<head>","<head><script>x</script>",StringComparison.Ordinal),source.Rcss);
        Reject(source.Rml.Replace("<rml>","<!DOCTYPE rml [<!ENTITY x SYSTEM 'file:///bad'>]><rml>",StringComparison.Ordinal),source.Rcss);
        Reject(source.Rml,source.Rcss+"\n#unknown { color: #ffffff; }");
        Reject(source.Rml,source.Rcss+"\nbody { background-image: url(secret); }");
        Reject(source.Rml.Replace("Ready / 准备就绪","{{dynamic}}",StringComparison.Ordinal),source.Rcss);
        Reject(source.Rml.Replace("<button id=\"game-start\">Start / 开始</button>","<p id=\"game-start\">Start</p>",StringComparison.Ordinal),source.Rcss);
        Check(sizeof(GameUiModel)==664&&sizeof(GameUiAction)==16,"game ABI layouts");
        Console.WriteLine($"PASS game UI authoring ({count} assertions)");return count;
    }
    public static int RunNative()
    {
        int count=0;void Check(bool ok,string label){if(!ok)throw new Exception("GAME UI: "+label);count++;}
        void Reject(Action action){try{action();}catch(InvalidOperationException){count++;return;}throw new Exception("Expected native rejection");}
        using var engine=new EngineHost(false,16);using var ui=new GameUiSession(engine);
        void Render()=>engine.Draw(new Camera{Zoom=1},ReadOnlySpan<Sprite>.Empty);
        ui.Load(GameUiSession.SourcePath);Render();uint generation=ui.State.Generation;
        Check(ui.State.Loaded==1&&ui.State.Pending==0,"staged game profile committed");
        generation=ui.Set(generation,MissionScreen.Title,"Courier / 运送任务","Carry the cell","Ready",90,false,false);Render();
        ui.TestPointer(generation,GameUiCommand.Start);Check(ui.Poll().Action==GameUiCommand.Start,"real Rml pointer hit-test start");
        Reject(()=>ui.Command(generation,GameUiCommand.Resume));Reject(()=>ui.Command(generation,GameUiCommand.Load));
        for(int i=0;i<70;i++)ui.Command(generation,GameUiCommand.Start);Check(ui.State.Queued==64&&ui.State.Overflow==6,"bounded game action queue");
        uint previous=generation;generation=ui.Set(generation,MissionScreen.Playing,"Mission","Deliver","Moving",88,true,false);Render();
        Check(generation!=previous&&ui.State.Queued==0&&ui.State.Overflow==0,"screen switch retires queued actions");Reject(()=>ui.Command(previous,GameUiCommand.Start));
        uint same=ui.Set(generation,MissionScreen.Playing,"Mission","Deliver","Moving",87,true,false);Check(same==generation,"timer-only patch preserves generation");
        ui.Command(generation,GameUiCommand.Pause);Check(ui.Poll().Action==GameUiCommand.Pause,"HUD pause explicit action");
        generation=ui.Set(generation,MissionScreen.Paused,"Paused","Deliver","Saved safely",87,true,true);Render();
        foreach(var command in new[]{GameUiCommand.Resume,GameUiCommand.Save,GameUiCommand.Load,GameUiCommand.Restart,GameUiCommand.Menu}){ui.Command(generation,command);Check(ui.Poll().Action==command,"pause action routed");}
        previous=generation;ui.Command(generation,GameUiCommand.Save);generation=ui.Set(generation,MissionScreen.Paused,"Paused","Deliver","Save unavailable",87,false,true);
        Check(generation!=previous&&ui.State.Queued==0,"availability change retires old actions");Reject(()=>ui.Command(generation,GameUiCommand.Save));
        foreach(var result in new[]{MissionScreen.Won,MissionScreen.Lost})
        {
            generation=ui.Set(generation,result,result.ToString(),"Deliver","Finished",0,false,true);Render();
            ui.Command(generation,GameUiCommand.Restart);Check(ui.Poll().Action==GameUiCommand.Restart,"result restart");Reject(()=>ui.Command(generation,GameUiCommand.Resume));
        }
        UiAction settingsAction=new(){Size=(uint)sizeof(UiAction)};Check(UiNative.Poll(engine.NativeContext,&settingsAction)!=0,"settings poll rejects game profile");
        GameUiModel invalid=new(){Size=(uint)sizeof(GameUiModel),Generation=generation,Screen=99};Check(GameUiNative.Set(engine.NativeContext,&invalid)!=0,"native screen bound");
        invalid.Screen=0;invalid.Seconds=10000;Check(GameUiNative.Set(engine.NativeContext,&invalid)!=0,"native timer bound");
        invalid.Seconds=0;invalid.Title[0]=0xff;Check(GameUiNative.Set(engine.NativeContext,&invalid)!=0,"native UTF8 bound");
        ui.TestFocus(false);Check((engine.Poll().Keys&Native.FocusLost)!=0,"SDL focus-lost event reported");Check(engine.Poll().Keys==Native.FocusLost,"focus-lost state persists without gameplay keys");
        ui.TestFocus(true);Check((engine.Poll().Keys&Native.FocusLost)==0,"SDL focus-gained clears state");
        string capture=Path.Combine(Environment.GetEnvironmentVariable("GAL_GAME_UI_EVIDENCE")??Path.GetTempPath(),"game-ui-title.bmp");
        generation=ui.Set(generation,MissionScreen.Title,"Courier Mission / 运送任务","Carry the cell through the east door to the archive.","Start a run, or load your saved checkpoint.",90,false,true);ui.Capture(capture);Render();
        ui.Dispose();ui.Dispose();using(var reopened=new GameUiSession(engine)){reopened.Load(GameUiSession.SourcePath);Render();Check(reopened.State.Generation!=generation&&reopened.State.Queued==0,"close and reopen cleanup");}
        Console.WriteLine($"PASS game UI native ({count} assertions; real Rml events, synthetic SDL focus, software-render compatible)");return count;
    }
}
