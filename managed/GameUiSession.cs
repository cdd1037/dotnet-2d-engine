using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
namespace GameAuthoringLab;
internal enum GameUiCommand:uint { None=0,Start=10,Resume=11,Save=12,Load=13,Restart=14,Menu=15,Pause=16 }
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct GameUiModel { public uint Size,Generation,Screen,Seconds,Flags,Reserved;public fixed byte Title[128];public fixed byte Objective[256];public fixed byte Status[256]; }
[StructLayout(LayoutKind.Sequential)]
internal struct GameUiAction { public uint Size,Generation;public GameUiCommand Action;public uint Reserved; }
internal static unsafe partial class GameUiNative
{
    [LibraryImport("gal",EntryPoint="gal_game_ui_open",StringMarshalling=StringMarshalling.Utf8)] [UnmanagedCallConv(CallConvs=[typeof(CallConvCdecl)])] internal static partial int Open(nint c,string path,string font);
    [LibraryImport("gal",EntryPoint="gal_game_ui_set_model")] [UnmanagedCallConv(CallConvs=[typeof(CallConvCdecl)])] internal static partial int Set(nint c,GameUiModel* model);
    [LibraryImport("gal",EntryPoint="gal_game_ui_poll_action")] [UnmanagedCallConv(CallConvs=[typeof(CallConvCdecl)])] internal static partial int Poll(nint c,GameUiAction* action);
    [LibraryImport("gal",EntryPoint="gal_game_ui_test_command")] [UnmanagedCallConv(CallConvs=[typeof(CallConvCdecl)])] internal static partial int Command(nint c,uint generation,uint action);
}
internal sealed unsafe class GameUiSession(EngineHost engine):UiSessionOwner(engine)
{
    public static string SourcePath=>new AssetRoot().FilePath("ui/game.rml");
    public UiState State {get{UiState state=new(){Size=(uint)sizeof(UiState)};Native.Check(UiNative.State(Context,&state),"game UI state");return state;}}
    public void Load(string path)
    {
        CheckAccess();Stage(GameUiAuthoring.ValidateFiles(path));
    }
    public void LoadAsset(AssetRoot assets, string logicalPath)
    {
        CheckAccess();Stage(GameUiAuthoring.ValidateAsset(assets,logicalPath));
    }
    private void Stage((string Rml,string Rcss) validated)
    {
        string staging=Path.Combine(Path.GetTempPath(),"gal-game-ui-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(staging);
        try
        {
            File.WriteAllText(Path.Combine(staging,"game.rml"),validated.Rml,new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(staging,"game.rcss"),validated.Rcss,new UTF8Encoding(false));
            Native.Check(GameUiNative.Open(Context,Path.Combine(staging,"game.rml"),Environment.GetEnvironmentVariable("GAL_UI_FONT")??"/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc"),"game UI stage");MarkNativeOpened();
        }
        finally{Directory.Delete(staging,true);}
    }
    public uint Set(uint generation,MissionScreen screen,string title,string objective,string status,int seconds,bool canSave,bool canLoad)
    {
        if(screen is <MissionScreen.Title or >MissionScreen.Lost||seconds is <0 or >9999)throw new ArgumentOutOfRangeException(nameof(screen));
        UiSettingsContract.ValidateText(title,127,127,"game.title");UiSettingsContract.ValidateText(objective,255,255,"game.objective");UiSettingsContract.ValidateText(status,255,255,"game.status");
        GameUiModel model=new(){Size=(uint)sizeof(GameUiModel),Generation=generation,Screen=(uint)screen,Seconds=(uint)seconds,Flags=(canSave?1u:0u)|(canLoad?2u:0u)};
        UiNative.Put(model.Title,128,title);UiNative.Put(model.Objective,256,objective);UiNative.Put(model.Status,256,status);
        Native.Check(GameUiNative.Set(Context,&model),"game UI model");return State.Generation;
    }
    public GameUiAction Poll(){GameUiAction action=new(){Size=(uint)sizeof(GameUiAction)};Native.Check(GameUiNative.Poll(Context,&action),"game UI action");return action;}
    public void Command(uint generation,GameUiCommand command)=>Native.Check(GameUiNative.Command(Context,generation,(uint)command),"game UI scripted command");
    internal void TestFocus(bool focused)=>Native.Check(GameUiNative.Command(Context,State.Generation,focused?101u:100u),"game UI focus probe");
    internal void TestPointer(uint generation,GameUiCommand command)=>Native.Check(GameUiNative.Command(Context,generation,200+(uint)command),"game UI pointer probe");
    public void Capture(string path)=>Native.Check(UiNative.Capture(Context,Path.GetFullPath(path)),"game UI capture");
}
