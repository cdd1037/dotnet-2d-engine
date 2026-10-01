using System.Diagnostics;
namespace GameAuthoringLab;

// Experiment-only host policy: a modal pauses simulation, not merely keyboard input.
// Require a neutral frame after dismissal so a held UI key cannot become gameplay.
internal sealed class ModalGameInput(RoomGame game)
{
    public bool Paused {get;private set;}
    private bool _waitForNeutral;
    public void Pause(){Paused=true;_waitForNeutral=true;game.ResetInputBoundary();}
    public void Resume(){Paused=false;_waitForNeutral=true;game.ResetInputBoundary();}
    public void Advance(uint keys,float elapsed)
    {
        if(Paused)return;
        if(_waitForNeutral){if(keys==0)_waitForNeutral=false;return;}
        game.Advance(keys,elapsed);
    }
}

internal static unsafe class RoomUiProbe
{
    public static int Run(bool scenario,int frames)
    {
        using var engine=new EngineHost(false,4096);
        using var bank=new TextureBank(engine,SampleAssets.Catalog());
        var game=new RoomGame();var inputGate=new ModalGameInput(game);
        var batch=new SpriteBatch(64){RegionResolver = bank.ResolveRegion};var camera=new Camera{Zoom=1};
        UiSession? ui=null;uint generation=0;int checks=0;
        void Check(bool ok,string label){if(!ok)throw new InvalidOperationException("ROOM UI: "+label);checks++;}
        void Render(){bank.Sync(game.World);game.World.ExtractSprites(batch);engine.Draw(camera,batch.RegionDraws);}
        void Open()
        {
            var candidate=new UiSession(engine);
            try{candidate.Load(UiProbe.SourcePath);Render();Check(candidate.State.Loaded==1,"menu committed");}
            catch{candidate.Dispose();throw;}
            ui=candidate;generation=ui.State.Generation;inputGate.Pause();
            ui.Set(generation,"Player / 玩家",65,"Escape: resume / 返回游戏");
        }
        void Close(){ui?.Dispose();ui=null;inputGate.Resume();}
        try
        {
            Render();
            if(scenario)
            {
                var start=game.Player.LocalTransform;
                inputGate.Advance(Native.Right,RoomGame.FixedDelta);
                Check(game.Player.LocalTransform.X>start.X,"movement before menu");
                // Queue an action without enough time for a step; opening must discard it.
                game.Player.LocalTransform=new Transform2D(230,280);
                game.Advance(Native.Interact,RoomGame.FixedDelta/4);
                Open();var paused=game.Player.LocalTransform;int ticks=0;
                game.Player.Behavior=new TickBehavior(()=>ticks++);
                ui!.Command(generation,3);ui.Command(generation,5);Render();
                Check(ui.State.KeyboardFocus==1,"text field owns focus");
                for(int i=0;i<120;i++)inputGate.Advance(Native.Right|Native.Interact|Native.Transition,RoomGame.FixedDelta);
                Check(game.Player.LocalTransform==paused&&ticks==0&&game.Held is null&&game.TransitionCount==0,"typing pauses movement/actions/behaviors");
                bool text=false;UiAction action;while((action=ui.Poll()).Action!=0)if(action.Action==3&&UiNative.Text(action.Name,128).Contains("星",StringComparison.Ordinal))text=true;
                Check(text,"committed text reaches Rml input");
                ui.Command(generation,1);uint old=generation;Close();
                UiState closedState=new(){Size=(uint)sizeof(UiState)};Check(UiNative.State(engine.NativeContext,&closedState)!=0,"close removes native UI context");
                inputGate.Advance(Native.Right|Native.Interact,10);
                Check(game.Player.LocalTransform==paused&&ticks==0,"held keys and elapsed time cannot leak across resume");
                inputGate.Advance(0,10);inputGate.Advance(Native.Right,RoomGame.FixedDelta);
                Check(game.Player.LocalTransform.X==paused.X+3&&ticks==1&&game.Held is null,"fresh input resumes one step, no pending pickup or catch-up");
                Open();Check(generation!=old&&ui!.State.Queued==0&&ui.State.KeyboardFocus==0,"reopen changes generation, clears queued actions and focus");
                bool rejected=false;try{ui!.Command(old,1);}catch(InvalidOperationException){rejected=true;}Check(rejected,"stale listener command rejected");
                // Programmatic scene replacement while modal: retire UI first, then room.
                ui!.Command(generation,1);Scene previous=game.ActiveScene;Close();
                game.Player.LocalTransform=new Transform2D(830,280);Check(game.UseDoor(),"room transition");Render();
                Check(!previous.IsLoaded&&game.Player.IsAlive&&game.Item.IsAlive,"room fixtures retire; persistent roles survive");
                Open();Check(ui!.State.Queued==0,"new room has no previous menu action");Close();Render();
                bank.Dispose();Check(engine.TextureCount==0,"texture ownership cleaned");
                Console.WriteLine($"ROOM UI SCENARIO PASS assertions={checks}; synthetic host input + real Rml events/software render; physical keyboard/IME unverified");return 0;
            }
            Console.WriteLine("ROOM UI EXPERIMENT | WASD/arrows, E/F/T | Escape opens/closes settings | window close quits | release keys after resume");
            var clock=Stopwatch.StartNew();double previousTime=clock.Elapsed.TotalSeconds;uint previousKeys=0;int count=0;
            while(frames==0||count<frames)
            {
                double now=clock.Elapsed.TotalSeconds;float dt=(float)Math.Clamp(now-previousTime,0,.25);previousTime=now;
                var input=engine.Poll();if(input.Quit!=0)break;
                if(Program.Pressed(input.Keys,previousKeys,Native.Escape)){if(ui is null)Open();else Close();}
                previousKeys=input.Keys;
                if(ui is not null)
                {
                    UiAction action;while((action=ui.Poll()).Action!=0)
                    {
                        if(action.Generation!=generation)continue;
                        if(action.Action==1)ui.Set(generation,UiNative.Text(action.Name,128),action.Volume,"Applied / 已应用 · Escape: resume");
                        else if(action.Action==2)ui.Set(generation,UiSettingsContract.DefaultPlayerName,UiSettingsContract.DefaultVolume,"Reset / 已重置 · Escape: resume");
                    }
                }
                inputGate.Advance(input.Keys,dt);Render();count++;Thread.Sleep(1);
            }
            return 0;
        }
        finally{ui?.Dispose();}
    }
    private sealed class TickBehavior(Action tick):IBehavior {public void Update(Entity entity,float dt)=>tick();}
}
