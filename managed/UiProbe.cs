using System.Text;
namespace GameAuthoringLab;

// An optional settings adapter; no World or RoomGame type depends on it.
internal sealed unsafe class UiSession : IDisposable
{
    private readonly EngineHost _engine;private bool _closed;
    private nint Context{get{ObjectDisposedException.ThrowIf(_closed,this);return _engine.NativeContext;}}
    public UiSession(EngineHost engine){_engine=engine;}
    public UiState State{get{var state=new UiState{Size=(uint)sizeof(UiState)};Native.Check(UiNative.State(Context,&state),"ui state");return state;}}
    public void Load(string path)
    {
        ObjectDisposedException.ThrowIf(_closed,this);
        var validated=UiAuthoring.ValidateFiles(path);
        string directory=Path.Combine(Path.GetTempPath(),"gal-ui-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try {
        File.WriteAllText(Path.Combine(directory,"settings.rml"),validated.Rml,new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(directory,"settings.rcss"),validated.Rcss,new UTF8Encoding(false));
        Native.Check(UiNative.Open(Context,Path.Combine(directory,"settings.rml"),Environment.GetEnvironmentVariable("GAL_UI_FONT")??"/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc"),"ui stage");
        } finally {Directory.Delete(directory,true);}
    }
    public void Set(uint generation,string name,int volume,string status)
    {
        UiSettingsContract.ValidateModel(name,volume,status);
        UiModel model=new(){Size=(uint)sizeof(UiModel),Generation=generation,Volume=volume};UiNative.Put(model.Name,128,name);UiNative.Put(model.Status,256,status);
        Native.Check(UiNative.SetModel(Context,&model),"ui model");
    }
    public UiAction Poll(){UiAction action=new(){Size=(uint)sizeof(UiAction)};Native.Check(UiNative.Poll(Context,&action),"ui event");return action;}
    public void Command(uint generation,uint command)=>Native.Check(UiNative.Command(Context,generation,command),"ui scripted input");
    public void Capture(string path)=>Native.Check(UiNative.Capture(Context,Path.GetFullPath(path)),"ui capture");
    public void Dispose(){if(_closed)return;Native.Check(UiNative.Close(Context),"ui close");_closed=true;}
}
internal static unsafe class UiProbe
{
    internal static string SourcePath=>Path.Combine(Environment.GetEnvironmentVariable("GAL_ASSET_ROOT")??Path.Combine(AppContext.BaseDirectory,"assets"),"ui","settings.rml");
    internal static int Run(bool scenario,int frames)
    {
        using var engine=new EngineHost(false,4096);using var bank=new TextureBank(engine,new AssetCatalog());var game=new RoomGame();var batch=new SpriteBatch(64){TextureResolver=bank.Resolve};var camera=new Camera{Zoom=1};
        void Render(){bank.Sync(game.World);game.World.ExtractSprites(batch);engine.Draw(camera,batch.Draws);}
        using var ui=new UiSession(engine);ui.Load(SourcePath);Render();var state=ui.State;
        if(state.Loaded!=1||state.Pending!=0)throw new InvalidOperationException("UI staging failed: "+UiNative.Text(state.Diagnostic,512));
        uint generation=state.Generation;int assertions=0;
        void Check(bool value,string message){if(!value)throw new InvalidOperationException("UI check: "+message);assertions++;}
        void ExpectFailure(Action action,string message){try{action();}catch(InvalidOperationException){assertions++;return;}throw new InvalidOperationException(message);}
        if(scenario)
        {
            string capture=Environment.GetEnvironmentVariable("GAL_UI_CAPTURE")??"ui-settings.bmp";
            ui.Capture(Path.ChangeExtension(capture,"initial.bmp"));Render();
            Check(sizeof(UiModel)==400&&sizeof(UiAction)==144&&sizeof(UiState)==544,"ABI layouts");
            UiModel invalid=new(){Size=(uint)sizeof(UiModel),Generation=generation,Volume=101};
            Check(UiNative.SetModel(engine.NativeContext,&invalid)!=0,"native bounds reject invalid volume");invalid.Volume=20;invalid.Name[0]=0xff;
            Check(UiNative.SetModel(engine.NativeContext,&invalid)!=0,"native invalid UTF8 rejected");
            UiState wrongSize=new();Check(UiNative.State(engine.NativeContext,&wrongSize)!=0,"size-versioned state rejected");
            nint nativeContext=engine.NativeContext;Check(Task.Run(()=>UiNative.Close(nativeContext)).GetAwaiter().GetResult()!=0,"native wrong-thread UI close rejected");
            Camera activeCamera=new(){Zoom=1};Native.Check(Native.Begin(nativeContext,&activeCamera),"test begin");Check(UiNative.Close(nativeContext)!=0,"UI close during frame rejected");Native.Check(Native.Abort(nativeContext),"test abort");
            ui.Set(generation,"林 River",72,"C# draft / 草稿");Render();
            // Ignore control change events emitted by SetValue; then exercise a real Rml event listener.
            while(ui.Poll().Action!=0){}
            ui.Command(generation,1);var applied=ui.Poll();
            Check(applied.Action==1&&applied.Generation==generation&&applied.Volume==72&&UiNative.Text(applied.Name,128)=="林 River","copied apply event preserves UTF8 model");
            ui.Set(generation,"林 River",72,"已应用 / Applied by C# · 72%");Render();
            ui.Command(generation,3);Check(ui.State.KeyboardFocus==1,"native text focus is active");
            ui.Command(generation,5);Render();var textEvent=ui.Poll();while(textEvent.Action!=0&&textEvent.Action!=3)textEvent=ui.Poll();
            Check(textEvent.Action==3&&UiNative.Text(textEvent.Name,128).Contains("星",StringComparison.Ordinal),"scripted committed UTF8 text path");
            ui.Command(generation,4);Render();Check(ui.State.ScrollTop>0,"rectangular list actually scrolls");
            Check(engine.Poll().Keys==0,"text focus suppresses gameplay snapshot (no physical input claim)");
            while(ui.Poll().Action!=0){}
            for(int i=0;i<70;i++)ui.Command(generation,1);
            Check(ui.State.Queued==64&&ui.State.Overflow==6,"bounded queue and explicit overflow");
            ui.Load(SourcePath);Check(ui.State.Pending==1,"reload staged");Render();
            uint newer=ui.State.Generation;Check(newer!=generation&&ui.State.Queued==0&&ui.State.Overflow==0,"reload invalidates old queued actions");
            ExpectFailure(()=>ui.Set(generation,"old",1,"stale"),"stale model was accepted");
            ExpectFailure(()=>ui.Command(generation,1),"stale command was accepted");
            generation=newer;
            // Native parser warning rejection preserves the live document independently of managed preflight.
            string bad=Path.Combine(Path.GetTempPath(),"gal-ui-bad-"+Guid.NewGuid().ToString("N")+".rml");
            File.WriteAllText(bad,"<rml><head><style>body { made-up-property: x; }</style></head><body><p>bad</p></body></rml>");
            try{Check(UiNative.Open(engine.NativeContext,bad,Environment.GetEnvironmentVariable("GAL_UI_FONT")??"/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc")!=0,"native malformed profile rejected");}finally{File.Delete(bad);}
            Check(ui.State.Generation==generation&&ui.State.Loaded==1,"failed reload retained live generation");Render();
            while(ui.Poll().Action!=0){}
            ui.Command(generation,6);Check(ui.Poll().Action==1,"scripted pointer hit-test click");
            ui.Command(generation,2);Check(ui.Poll().Action==2,"reset routed to managed action");
            ui.Set(generation,"林 River",72,"已应用 / Applied by C# · 72%");ui.Command(generation,4);Render();
            ui.Capture(capture);Render();
            ui.Dispose();ui.Dispose();Check(UiNative.Close(engine.NativeContext)==0,"native repeated UI close safe");Render();
            using(var reopened=new UiSession(engine)){reopened.Load(SourcePath);Render();Check(reopened.State.Loaded==1&&reopened.State.Generation!=generation,"close/reopen generation and cleanup");}
            Render();bank.Dispose();Check(engine.TextureCount==0,"world resources cleaned");
            Console.WriteLine($"UI SCENARIO PASS assertions={assertions} frames={engine.GetStats().Frames} screenshot={Path.GetFullPath(capture)}; scripted software renderer, real IME unverified");return 0;
        }
        int count=0;while(frames==0||count<frames){var input=engine.Poll();if(input.Quit!=0)break;UiAction action;while((action=ui.Poll()).Action!=0){if(action.Action==1)ui.Set(generation,UiNative.Text(action.Name,128),action.Volume,"已应用 / Applied by C#");else if(action.Action==2)ui.Set(generation,UiSettingsContract.DefaultPlayerName,UiSettingsContract.DefaultVolume,"已重置 / Reset");}Render();count++;Thread.Sleep(1);}return 0;
    }
}
