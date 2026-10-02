using System.Runtime.InteropServices;
using GameAuthoringLab;

// UiModelExamples.cs and its authored assets are copied into this independent
// package consumer by the harness. They are sample application code, not runtime.
if(UiModelExamples.Run(9)!=0)throw new Exception("Three-fixture run failed");
var assets=new AssetRoot(Path.Combine(AppContext.BaseDirectory,"assets"));
using var engine=EngineHost.Create(maxSprites:32);
void Draw()=>engine.Draw(new Camera{Zoom=1},ReadOnlySpan<Sprite>.Empty);
int checks=0;
void Check(bool success,string message){if(!success)throw new Exception(message);checks++;}
UiCommandArgument Key(ulong value)=>new(UiValueKind.Key,"",0,value);
UiCommandArgument Bool(bool value)=>new(UiValueKind.Boolean,"",value?1:0,0);
UiCommandEvent Packet<T>(UiModelSession<T> ui,uint id,params UiCommandArgument[] arguments)
{
    var status=ui.Status;
    return new(status.Generation,status.Revision,id,arguments.Length,
        arguments.Length>0?arguments[0]:default,arguments.Length>1?arguments[1]:default,
        arguments.Length>2?arguments[2]:default,arguments.Length>3?arguments[3]:default);
}

var inventory=UiModelExamples.Inventory();
using(var ui=new UiModelSession<UiModelExamples.InventoryModel>(engine,UiModelExamples.InventorySchema(),UiModelExamples.InventoryCommands()))
{
    ui.LoadAsset(assets,UiModelExamples.InventoryAsset,UiModelExamples.InventoryImages);Draw();
    Check(ui.Apply(inventory),"First inventory projection");Draw();
    Check(!ui.Apply(inventory),"Unchanged inventory projection");
    engine.PollInput();SdlInput.Click(480,213);engine.PollInput();
    var native=ui.Poll();
    Check(native.CommandId==UiModelExamples.Equip&&native.Count==1&&native[0].Kind==UiValueKind.Key&&native[0].Key==inventory.Items[0].Id&&ui.IsCurrent(native),"Real SDL click returns copied exact 64-bit key");
    Check(UiModelExamples.Handle(inventory,native)&&!inventory.Items[0].Equipped,"Real native packet dispatches application handler");
    ui.Apply(inventory);Draw();Check(!ui.IsCurrent(native),"Real native packet becomes stale after update");
    var old=Packet(ui,UiModelExamples.Equip,Key(inventory.Items[0].Id));
    Check(old[0].Key==9_007_199_254_740_993UL&&ui.IsCurrent(old),"Exact first key");
    SdlInput.Button(480,213,true);engine.PollInput();
    inventory.Items.Reverse();Check(ui.Apply(inventory),"Reorder publishes");Draw();
    SdlInput.Button(480,213,false);engine.PollInput();
    Check(ui.Poll().IsEmpty,"Real pointer-down reorder/up cannot choose replacement row");
    SdlInput.Click(480,213);engine.PollInput();var reordered=ui.Poll();
    Check(reordered.CommandId==UiModelExamples.Equip&&reordered[0].Key==inventory.Items[0].Id&&ui.IsCurrent(reordered),"Fresh real click routes to reordered exact key");
    Check(!ui.IsCurrent(old),"Reorder rejects earlier revision");
    var exact=Packet(ui,UiModelExamples.Equip,Key(ulong.MaxValue-1));
    Check(ui.IsCurrent(exact)&&UiModelExamples.Handle(inventory,exact),"Reordered exact 64-bit identity");
    Check(inventory.Items.Single(x=>x.Id==ulong.MaxValue-1).Equipped,"Identity updates the intended item");
    ui.Apply(inventory);Draw();
    var drop=Packet(ui,UiModelExamples.Drop,Key(ulong.MaxValue-1));
    Check(ui.IsCurrent(drop)&&UiModelExamples.Handle(inventory,drop),"Drop removes intended item");
    ui.Apply(inventory);Draw();
    Check(!ui.IsCurrent(drop)&&!ui.IsCurrent(Packet(ui,UiModelExamples.Equip,Key(ulong.MaxValue-1))),"Removed key is stale");
    var beforeReload=Packet(ui,UiModelExamples.Equip,Key(inventory.Items[0].Id));
    ui.LoadAsset(assets,UiModelExamples.InventoryAsset,UiModelExamples.InventoryImages);Draw();
    Check(ui.Status.Revision==0&&!ui.IsCurrent(beforeReload),"Reload invalidates prior generation");
    ui.Apply(inventory);Draw();
    Check(ui.Status.Loaded&&ui.Status.Overflow==0,"Inventory published cleanly");
}
var dialogue=UiModelExamples.Dialogue();
using(var ui=new UiModelSession<UiModelExamples.DialogueModel>(engine,UiModelExamples.DialogueSchema(),UiModelExamples.DialogueCommands()))
{
    ui.LoadAsset(assets,UiModelExamples.DialogueAsset);Draw();ui.Apply(dialogue);Draw();
    var choice=dialogue.Choices[0];
    var command=Packet(ui,UiModelExamples.Choose,Key(choice.Id),new(UiValueKind.Text,choice.Text,0,0));
    Check(ui.IsCurrent(command)&&UiModelExamples.Handle(dialogue,command),"Dialogue typed key/text handler");
    ui.Apply(dialogue);Draw();Check(!ui.IsCurrent(command),"Dialogue earlier revision rejected");
}
var settings=UiModelExamples.Settings();
settings.Profile.Name="";
using(var ui=new UiModelSession<UiModelExamples.SettingsModel>(engine,UiModelExamples.SettingsSchema(),UiModelExamples.SettingsCommands()))
{
    ui.LoadAsset(assets,UiModelExamples.SettingsAsset);Draw();ui.Apply(settings);Draw();
    engine.PollInput();SdlInput.Click(150,210);engine.PollInput();
    Check(ui.Poll().IsEmpty,"Focusing text input does not fabricate a command");
    string accepted="";
    foreach(string input in new[]{"a","b","c","d"})
    {
        SdlInput.Text(engine,input);var typed=ui.Poll();accepted+=input;
        Check(typed.CommandId==UiModelExamples.Rename&&typed.Count==1&&typed[0].Kind==UiValueKind.Text&&typed[0].Text==accepted&&ui.IsCurrent(typed),"Continuous public SDL typing preserves focus/caret");
        Check(UiModelExamples.Handle(settings,typed)&&settings.Profile.Name==accepted,"Application accepts each growing text packet");
        ui.Apply(settings);Draw();Check(!ui.IsCurrent(typed)&&ui.Poll().IsEmpty,"Accepted text advances revision and drains input queue");
        settings.Status="Unrelated update after "+accepted;ui.Apply(settings);Draw();
    }
    SdlInput.Text(engine,"x");var draft=ui.Poll();
    Check(draft.CommandId==UiModelExamples.Rename&&draft[0].Text=="abcdx"&&settings.Profile.Name=="abcd","Unaccepted text stays a local draft");
    settings.Status="Another unrelated scalar update";ui.Apply(settings);Draw();
    SdlInput.Text(engine,"y");var continued=ui.Poll();
    Check(continued.CommandId==UiModelExamples.Rename&&continued[0].Text=="abcdxy"&&ui.IsCurrent(continued),"Unrelated scalar update preserves local draft and focus");
    Check(UiModelExamples.Handle(settings,continued)&&settings.Profile.Name=="abcdxy","Preserved draft remains application-authorized");
    ui.Apply(settings);Draw();
    var option=settings.Groups[1].Options[1];
    var command=Packet(ui,UiModelExamples.SetOption,Key(option.Id),Bool(false));
    Check(ui.IsCurrent(command)&&UiModelExamples.Handle(settings,command)&&!option.Enabled,"Nested setting typed key/bool handler");
    ui.Apply(settings);Draw();Check(!ui.IsCurrent(command),"Nested setting earlier revision rejected");
    Check(ui.Poll().IsEmpty&&ui.Status.Overflow==0,"No fabricated native events");
}
Console.WriteLine($"PACKAGE MODEL UI PASS fixtures=3 rendered_frames=9 public_contract_checks={checks} identity=uint64 reorder=stale-safe native_events=SDL-public-ABI continuous_typing=4 draft_preserved=true");
var nativePaths=File.ReadAllLines("/proc/self/maps").Where(x=>x.EndsWith("/libgal.so",StringComparison.Ordinal)).Select(x=>x[x.IndexOf('/')..]).Distinct().ToArray();
if(nativePaths.Length!=1||!nativePaths[0].StartsWith(AppContext.BaseDirectory,StringComparison.Ordinal))throw new Exception("Native runtime did not load from the copied publish directory");
Console.WriteLine("NATIVE_LOADED "+nativePaths[0]);

// Independent declarations of the public pinned SDL 3.4.16 event ABI.
// No engine native context, runtime-internal declarations, or reflection access.
static unsafe class SdlInput
{
    [StructLayout(LayoutKind.Explicit,Size=128)]
    private struct Event
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(16)] public uint WindowId;
        [FieldOffset(24)] public byte Button;
        [FieldOffset(24)] public nint Text;
        [FieldOffset(25)] public byte Down;
        [FieldOffset(26)] public byte Clicks;
        [FieldOffset(28)] public float X;
        [FieldOffset(32)] public float Y;
    }
    [DllImport("libSDL3.so.0",CallingConvention=CallingConvention.Cdecl)] private static extern nint* SDL_GetWindows(out int count);
    [DllImport("libSDL3.so.0",CallingConvention=CallingConvention.Cdecl)] private static extern uint SDL_GetWindowID(nint window);
    [DllImport("libSDL3.so.0",CallingConvention=CallingConvention.Cdecl)] private static extern void SDL_free(nint memory);
    [DllImport("libSDL3.so.0",CallingConvention=CallingConvention.Cdecl)] [return:MarshalAs(UnmanagedType.I1)] private static extern bool SDL_PushEvent(ref Event value);
    private static uint Window()
    {
        var windows=SDL_GetWindows(out int count);
        try{if(windows is null||count!=1)throw new Exception("Expected one package-owned SDL window");return SDL_GetWindowID(windows[0]);}
        finally{SDL_free((nint)windows);}
    }
    public static void Button(float x,float y,bool down)
    {
        var motion=new Event{Type=0x400,WindowId=Window(),X=x,Y=y};
        if(!SDL_PushEvent(ref motion))throw new Exception("SDL motion rejected");
        var button=new Event{Type=down?0x401u:0x402u,WindowId=motion.WindowId,Button=1,Down=down?(byte)1:(byte)0,Clicks=1,X=x,Y=y};
        if(!SDL_PushEvent(ref button))throw new Exception("SDL button rejected");
    }
    public static void Text(EngineHost engine,string text)
    {
        nint storage=Marshal.StringToCoTaskMemUTF8(text);
        try
        {
            var value=new Event{Type=0x303,WindowId=Window(),Text=storage};
            if(!SDL_PushEvent(ref value))throw new Exception("SDL text event rejected");
            engine.PollInput(); // Keep the text storage alive until the event is consumed.
        }
        finally{Marshal.FreeCoTaskMem(storage);}
    }
    public static void Click(float x,float y){Button(x,y,true);Button(x,y,false);}
}
