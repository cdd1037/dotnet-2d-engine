using System.Numerics;
using System.Runtime.InteropServices;
namespace GameAuthoringLab;

internal static unsafe class InputTests
{
    public static int Run()
    {
        int count=0;void Check(bool condition,string label){count++;if(!condition)throw new InvalidOperationException("INPUT: "+label);}
        void Reject(Action action,string label){try{action();}catch(Exception e)when(e is ArgumentException or InvalidOperationException){count++;return;}throw new InvalidOperationException("INPUT accepted: "+label);}
        Check(sizeof(InputSnapshot)==472&&Marshal.OffsetOf<InputSnapshot>(nameof(InputSnapshot.KeysDown))==80&&Marshal.OffsetOf<InputSnapshot>(nameof(InputSnapshot.Consumed))==464,"v2 interop size and offsets");
        using(var engine=new EngineHost(true,4))
        {
            var input=engine.PollInput();Check(input.Version==2&&input.Focused&&input.Drawable&&input.WindowWidth==960&&input.PixelWidth==960,"headless dimensions and flags");
            Check(input.ButtonsDown==0&&!input.KeyPressed(PhysicalKey.E,true),"headless input has no device events");
            InputSnapshot invalid=new(){Size=(uint)sizeof(InputSnapshot)-1,Version=2,Quit=77};
            Check(Native.PollV2(engine.NativeContext,&invalid)!=0&&invalid.Quit==77,"invalid size rejects without partial overwrite");
            invalid.Size=(uint)sizeof(InputSnapshot);invalid.Version=1;Check(Native.PollV2(engine.NativeContext,&invalid)!=0,"version rejected");
            invalid.Version=2;invalid.Reserved=1;Check(Native.PollV2(engine.NativeContext,&invalid)!=0,"reserved field rejected");
            Reject(()=>Task.Run(()=>engine.PollInput()).GetAwaiter().GetResult(),"wrong-thread poll");
            Camera camera=new(){Zoom=1};Native.Check(Native.Begin(engine.NativeContext,&camera),"input test begin");
            invalid.Reserved=0;Check(Native.PollV2(engine.NativeContext,&invalid)!=0,"poll prohibited during frame");Native.Check(Native.Abort(engine.NativeContext),"input test abort");
            Check(engine.Poll().Width==960&&engine.PollInput().PixelWidth==960,"v1/v2 layout compatibility on separate polls");
        }
        foreach(var view in new[]{new Viewport(960,540,960,540),new Viewport(960,540,1920,1080),new Viewport(800,600,1600,900),new Viewport(384,288,384,288)})
        {
            Vector2 point=new(125.25f,98.75f);Camera camera=new(){X=-53.5f,Y=21.25f,Zoom=1.75f};
            Check(view.TryWindowToPixels(point,out var pixels)&&view.TryPixelsToWindow(pixels,out var back)&&Vector2.Distance(back,point)<.001f,"logical/pixel round trip including nonuniform density");
            Check(view.TryWindowToWorld(point,camera,out var world)&&view.TryWorldToWindow(world,camera,out back)&&Vector2.Distance(back,point)<.001f,"window/world camera round trip");
        }
        var highDpi=new Viewport(960,540,1920,1080);
        Check(highDpi.TryWindowToWorld(new(100,50),new Camera{Zoom=2,X=10,Y=20},out var highPoint)&&highPoint==new Vector2(110,70),"2x pointer matches framebuffer camera units");
        foreach(var view in new[]{new Viewport(0,540,960,540),new Viewport(960,540,0,540),new Viewport(-1,540,960,540),new Viewport(960,540,960,540,false),new Viewport(20000,540,960,540)})
            Check(!view.TryWindowToWorld(Vector2.One,new Camera{Zoom=1},out var failed)&&failed==default,"invalid/minimized viewport has explicit conversion failure");
        Check(!highDpi.TryWorldToWindow(new(float.NaN,0),new Camera{Zoom=1},out _),"nonfinite point rejected");
        Check(!highDpi.TryWindowToWorld(Vector2.One,new Camera{Zoom=0},out _),"invalid camera rejected");
        Check(!highDpi.TryWorldToWindow(new(float.MaxValue,0),new Camera{X=-float.MaxValue,Zoom=100},out _),"conversion overflow rejected");
        Check(highDpi.TryWorldToWindow(new(float.MaxValue,0),new Camera{X=float.MaxValue,Zoom=100},out var cancelled)&&cancelled==Vector2.Zero,"double intermediates allow representable cancellation");

        var map=new InputActionMap(InputBinding.Key(1,PhysicalKey.E));
        var tap=Snapshot();SetKey(ref tap,PhysicalKey.E,false,true,true);
        Check(map.Update(tap)==new ActionState(0,1,1),"quick tap survives one poll as both edges");
        Check(map.Update(Snapshot())==default,"edges do not repeat next poll");
        Check(map.Update(tap)==new ActionState(0,1,1),"consecutive frame taps are independent");
        var held=Snapshot();SetKey(ref held,PhysicalKey.E,true,true,false);
        Check(map.Update(held)==new ActionState(1,1,0),"held action begins once");
        SetKey(ref held,PhysicalKey.E,true,false,false);Check(map.Update(held)==new ActionState(1,0,0),"held snapshot does not retrigger");
        var repressed=Snapshot();SetKey(ref repressed,PhysicalKey.E,true,true,true);Check(map.Update(repressed)==new ActionState(1,1,1),"release/repress of held key between polls retains both action edges");
        var lost=Snapshot();lost.Flags&=~InputFlags.Focused;Check(map.Update(lost)==new ActionState(0,0,1),"focus loss releases mapped action");
        Check(map.Update(held)==default,"focus regain waits for raw neutral");map.Update(Snapshot());Check(map.Update(tap).Pressed==1,"fresh tap works after neutral");
        map.Rebind(InputBinding.Key(1,PhysicalKey.F));var rebindHeld=Snapshot();SetKey(ref rebindHeld,PhysicalKey.F,true,false,false);
        Check(map.Update(rebindHeld)==default,"rebind does not activate a held newly bound key");map.Update(Snapshot());
        var f=Snapshot();SetKey(ref f,PhysicalKey.F,false,true,true);Check(map.Update(f).Pressed==1&&map.Update(tap).Pressed==0,"remapping uses copied configured control");
        Reject(()=>map.Rebind(InputBinding.Key(3,PhysicalKey.E)),"multibit action");Reject(()=>map.Rebind(InputBinding.Key(1,(PhysicalKey)512)),"out of range key");
        Check(map.Update(f).Pressed==1,"invalid rebind retains previous usable bindings");
        var alternatives=new InputActionMap(InputBinding.Key(1,PhysicalKey.A),InputBinding.Key(1,PhysicalKey.Left));
        var both=Snapshot();SetKey(ref both,PhysicalKey.A,true,true,false);SetKey(ref both,PhysicalKey.Left,true,true,false);Check(alternatives.Update(both)==new ActionState(1,1,0),"alternative bindings start one action");
        SetKey(ref both,PhysicalKey.A,true,false,false);SetKey(ref both,PhysicalKey.Left,false,true,true);Check(alternatives.Update(both)==new ActionState(1,0,0),"alternative tap cannot retrigger another continuously held binding");
        SetKey(ref both,PhysicalKey.Left,true,true,false);alternatives.Update(both);
        SetKey(ref both,PhysicalKey.A,false,false,true);SetKey(ref both,PhysicalKey.Left,true,false,false);Check(alternatives.Update(both)==new ActionState(1,0,0),"one alternative release does not release held action");
        SetKey(ref both,PhysicalKey.Left,false,false,true);Check(alternatives.Update(both)==new ActionState(0,0,1),"last alternative release ends action");
        var consumed=Snapshot();SetKey(ref consumed,PhysicalKey.E,true,true,false,true);SetKey(ref consumed,PhysicalKey.Escape,false,true,true,true);
        var routing=new InputActionMap(InputBinding.Key(1,PhysicalKey.E),InputBinding.Key(2,PhysicalKey.Escape,true));Check(routing.Update(consumed)==new ActionState(0,2,2),"raw consumed escape opt-in is explicit and gameplay key stays suppressed");
        var pointerMap=new InputActionMap(InputBinding.Button(4,PointerButton.Left));var pointer=Snapshot();pointer.ButtonsPressed=pointer.ButtonsReleased=pointer.GameButtonsPressed=pointer.GameButtonsReleased=1;
        Check(pointerMap.Update(pointer)==new ActionState(0,4,4),"mouse buttons can map without entity callbacks");pointer.GameButtonsPressed=pointer.GameButtonsReleased=0;Check(pointerMap.Update(pointer)==default,"consumed pointer never reaches gameplay binding");
        var noAllocation=InputActionMap.CreateSample();for(int i=0;i<128;i++)noAllocation.Update(held);long before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<1000;i++)noAllocation.Update(held);
        Check(GC.GetAllocatedBytesForCurrentThread()==before,"warmed action mapping allocates zero bytes");
        var catalog=SampleAssets.Catalog();using(var mission=new MissionGame(catalog.Assets.Resolve("relay.mission.json"),catalog))
        {
            mission.Start();mission.Advance(0,0,0);mission.Room.Player.LocalTransform=new Transform2D(230,280);
            mission.Advance(0,Native.Interact,RoomGame.FixedDelta/2);Check(mission.Room.Held is null,"quick interaction edge waits for fixed tick");
            mission.Advance(0,0,RoomGame.FixedDelta/2);Check(mission.Room.Held is not null,"fixed-step game consumes quick tap despite no held snapshot");
            mission.Pause();mission.Resume();mission.Advance(0,Native.Drop,RoomGame.FixedDelta);Check(mission.WaitingForNeutral&&mission.Room.Held is not null,"modal neutral gate also blocks transient tap edges");
        }
        Console.WriteLine($"INPUT SELF-TEST PASS assertions={count}; headless ABI, synthetic density math, action maps and fixed-step edges");return count;
    }

    internal static InputSnapshot Snapshot()=>new(){Size=(uint)sizeof(InputSnapshot),Version=2,Flags=InputFlags.Focused|InputFlags.Drawable,WindowWidth=960,WindowHeight=540,PixelWidth=960,PixelHeight=540};
    private static void SetKey(ref InputSnapshot s,PhysicalKey key,bool down,bool pressed,bool released,bool consumed=false)
    {
        int code=(int)key,word=code/64;ulong bit=1ul<<(code%64);
        fixed(ulong* d=s.KeysDown,p=s.KeysPressed,r=s.KeysReleased,gd=s.GameKeysDown,gp=s.GameKeysPressed,gr=s.GameKeysReleased)
        {d[word]=down?d[word]|bit:d[word]&~bit;p[word]=pressed?p[word]|bit:p[word]&~bit;r[word]=released?r[word]|bit:r[word]&~bit;
         gd[word]=down&&!consumed?gd[word]|bit:gd[word]&~bit;gp[word]=pressed&&!consumed?gp[word]|bit:gp[word]&~bit;gr[word]=released&&!consumed?gr[word]|bit:gr[word]&~bit;}
    }

    public static int RunGraphics()
    {
        int count=0;void Check(bool condition,string label){count++;if(!condition)throw new InvalidOperationException("INPUT GRAPHICS: "+label);}
        using var engine=new EngineHost(false,16);using var ui=new UiSession(engine);
        void Render()=>engine.Draw(new Camera{Zoom=1},ReadOnlySpan<Sprite>.Empty);
        ui.LoadAsset(new AssetRoot(),"ui/settings.rml");Render();uint generation=ui.State.Generation;
        engine.PollInput(); // Drain initial window events before queued test input.
        ui.Command(generation,7);var tap=engine.PollInput();
        Check(tap.KeyPressed(PhysicalKey.E,true)&&tap.KeyReleased(PhysicalKey.E,true)&&!tap.KeyDown(PhysicalKey.E,true),"queued SDL quick tap retains both physical edges");
        Check(tap.KeyPressed(PhysicalKey.E)&&tap.KeyReleased(PhysicalKey.E),"unhandled key tap reaches routed snapshot");
        Check(!engine.PollInput().KeyPressed(PhysicalKey.E,true),"poll consumes edge once");
        ui.Command(generation,3);ui.Command(generation,9);var captured=engine.PollInput();
        Check(captured.KeyDown(PhysicalKey.E,true)&&!captured.KeyDown(PhysicalKey.E)&&(captured.Consumed&InputConsumption.Keyboard)!=0,"text focus consumes key while raw state remains visible");
        for(int cycle=0;cycle<3;cycle++)
        {
            while(ui.Poll().Action!=0){}ui.Command(generation,8);var click=engine.PollInput();Render();
            Check(click.ButtonPressed(PointerButton.Left,true)&&click.ButtonReleased(PointerButton.Left,true)&&!click.ButtonDown(PointerButton.Left,true),"queued SDL click reports press/release");
            Check(!click.ButtonPressed(PointerButton.Left)&&(click.Consumed&InputConsumption.Pointer)!=0,"Rml handled click excluded from gameplay");
            int applied=0;UiAction action;while((action=ui.Poll()).Action!=0)if(action.Action==1)applied++;
            Check(applied==1,"repeated consumed click dispatches exactly one UI action");
        }
        Check(!engine.PollInput().KeyDown(PhysicalKey.E),"held captured key cannot leak after text field loses focus");
        ui.Command(generation,10);engine.PollInput();ui.Command(generation,7);Check(engine.PollInput().KeyPressed(PhysicalKey.E),"release then new key press regains gameplay routing");
        ui.Command(generation,11);var wheel=engine.PollInput();Render();
        Check(wheel.WheelY==-1&&wheel.GameWheelY==0&&(wheel.Consumed&InputConsumption.Wheel)!=0,"scroll wheel consumed by list never reaches gameplay");
        Check(ui.State.ScrollTop>0,"queued wheel actually scrolls Rml list");
        ui.Command(generation,9);var held=engine.PollInput();Check(held.KeyDown(PhysicalKey.E,true),"queued down persists as physical state");
        ui.Command(generation,17);var lost=engine.PollInput();Check(!lost.Focused&&!lost.KeyDown(PhysicalKey.E,true)&&lost.KeyReleased(PhysicalKey.E,true),"focus loss clears down state and exposes release");
        Check(!engine.PollInput().Focused,"focus loss persists without new events");ui.Command(generation,18);Check(engine.PollInput().Focused,"focus gain restores input availability");
        uint beforeResizeDraws=engine.GetStats().DrawCalls;ui.Command(generation,13);
        engine.Draw(new Camera{Zoom=1},new Sprite[]{new(){Width=4,Height=4,R=1,G=1,B=1,A=1}});
        Check(engine.GetStats().DrawCalls==beforeResizeDraws,"resize between poll and draw skips stale projection");
        var small=engine.PollInput();Check(small.Drawable&&small.WindowWidth==384&&small.WindowHeight==288&&small.PixelWidth>0,"actual SDL resize reports separate valid dimensions");Render();
        ui.Command(generation,15);var minimized=engine.PollInput();Check(!minimized.Drawable&&!minimized.Viewport.IsValid,"minimize event disables coordinate/render availability");
        uint draws=engine.GetStats().DrawCalls;Render();Check(engine.GetStats().DrawCalls==draws,"nondrawable frame skips graphics work");
        ui.Command(generation,16);ui.Command(generation,14);Check(engine.PollInput().Drawable,"restore re-enables drawable viewport");Render();
        ui.Command(generation,8);ui.Dispose();var afterClose=engine.PollInput();
        Check(afterClose.ButtonPressed(PointerButton.Left)&&afterClose.ButtonReleased(PointerButton.Left),"closed UI no longer consumes queued pointer input");
        Check((afterClose.Consumed&InputConsumption.Pointer)==0,"closed UI leaves no stale input capture");
        Render();Console.WriteLine($"INPUT GRAPHICS PASS assertions={count}; queued SDL events, real Rml consumption, software viewport checks; physical input/high-DPI hardware unverified");return count;
    }
}
