using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
namespace GameAuthoringLab;

public readonly record struct UiBindingStatus(uint Generation,uint Revision,bool Loaded,bool Pending,uint Queued,uint Overflow,string Diagnostic);
public enum UiBindingKind:uint { Text=1,TextInput=2,Boolean=3,Number=4,Action=5,List=6 }
public readonly record struct UiBindingTarget(string ElementId,UiBindingKind Kind,uint ActionId=0);
public readonly record struct UiListRow(ulong Id,string Text,bool Enabled=true,bool Selected=false);
public readonly record struct UiBindingAction(uint Generation,uint Revision,int Target,uint ActionId,UiBindingKind Kind,ulong RowId,string Text,int Number,bool Boolean)
{ public bool IsEmpty=>ActionId==0; }

/// <summary>Explicit C# projections. Built registrations are copied into each session; no reflection or native callbacks.</summary>
public sealed class UiBindings<T>
{
    internal sealed record Binding(UiBindingTarget Target,Func<T,string>? Text=null,Func<T,bool>? Boolean=null,
        Func<T,int>? Number=null,Func<T,IReadOnlyList<UiListRow>>? Rows=null,Func<T,bool>? Enabled=null);
    private readonly List<Binding> _bindings=[];
    private UiBindings<T> Add(Binding binding)
    {
        if(_bindings.Count==32)throw new ArgumentException("UI supports at most 32 binding targets.");
        if(_bindings.Any(b=>b.Target.ElementId==binding.Target.ElementId))throw new ArgumentException("Duplicate UI binding target ID.");
        BoundUiAuthoring.ValidateSchema([binding.Target]);_bindings.Add(binding);return this;
    }
    public UiBindings<T> Text(string id,Func<T,string> read){ArgumentNullException.ThrowIfNull(read);return Add(new(new(id,UiBindingKind.Text),Text:read));}
    public UiBindings<T> TextInput(string id,Func<T,string> read,uint action=0,Func<T,bool>? enabled=null){ArgumentNullException.ThrowIfNull(read);return Add(new(new(id,UiBindingKind.TextInput,action),Text:read,Enabled:enabled));}
    public UiBindings<T> Boolean(string id,Func<T,bool> read,uint action=0,Func<T,bool>? enabled=null){ArgumentNullException.ThrowIfNull(read);return Add(new(new(id,UiBindingKind.Boolean,action),Boolean:read,Enabled:enabled));}
    public UiBindings<T> Number(string id,Func<T,int> read,uint action=0,Func<T,bool>? enabled=null){ArgumentNullException.ThrowIfNull(read);return Add(new(new(id,UiBindingKind.Number,action),Number:read,Enabled:enabled));}
    public UiBindings<T> Action(string id,uint action,Func<T,bool>? enabled=null){if(action==0)throw new ArgumentOutOfRangeException(nameof(action));return Add(new(new(id,UiBindingKind.Action,action),Enabled:enabled));}
    public UiBindings<T> List(string id,Func<T,IReadOnlyList<UiListRow>> read,uint action,Func<T,bool>? enabled=null){ArgumentNullException.ThrowIfNull(read);if(action==0)throw new ArgumentOutOfRangeException(nameof(action));return Add(new(new(id,UiBindingKind.List,action),Rows:read,Enabled:enabled));}
    internal Binding[] Freeze(){if(_bindings.Count==0)throw new ArgumentException("At least one UI binding is required.");return _bindings.ToArray();}
}
[StructLayout(LayoutKind.Sequential)] internal unsafe struct BoundTarget { public uint Size,Kind,Action,Reserved;public fixed byte Id[48]; }
[StructLayout(LayoutKind.Sequential)] internal unsafe struct BoundValue { public uint Size,Target,Flags,RowFirst,RowCount,Reserved;public double Number;public fixed byte Text[256]; }
[StructLayout(LayoutKind.Sequential)] internal unsafe struct BoundRow { public ulong Id;public uint Flags,Reserved;public fixed byte Text[256]; }
[StructLayout(LayoutKind.Sequential)] internal struct BoundSnapshot { public uint Size,Version,Generation,Revision,ValueCount,RowCount; }
[StructLayout(LayoutKind.Sequential)] internal unsafe struct BoundAction { public uint Size,Generation,Revision,Target,Action,Kind;public ulong Row;public double Number;public uint Flags,Reserved;public fixed byte Text[256]; }
internal static unsafe partial class BoundUiNative
{
    [LibraryImport("gal",EntryPoint="gal_bound_ui_open",StringMarshalling=StringMarshalling.Utf8)] [UnmanagedCallConv(CallConvs=[typeof(CallConvCdecl)])] internal static partial int Open(nint context,string path,string font,BoundTarget* targets,uint count);
    [LibraryImport("gal",EntryPoint="gal_bound_ui_open_images",StringMarshalling=StringMarshalling.Utf8)] [UnmanagedCallConv(CallConvs=[typeof(CallConvCdecl)])] internal static partial int OpenImages(nint context,string path,string font,BoundTarget* targets,uint count,byte** imagePaths,uint imageCount);
    [LibraryImport("gal",EntryPoint="gal_bound_ui_apply")] [UnmanagedCallConv(CallConvs=[typeof(CallConvCdecl)])] internal static partial int Apply(nint context,BoundSnapshot* snapshot,BoundValue* values,BoundRow* rows);
    [LibraryImport("gal",EntryPoint="gal_bound_ui_poll")] [UnmanagedCallConv(CallConvs=[typeof(CallConvCdecl)])] internal static partial int Poll(nint context,BoundAction* action);
    [LibraryImport("gal",EntryPoint="gal_bound_ui_test_command")] [UnmanagedCallConv(CallConvs=[typeof(CallConvCdecl)])] internal static partial int Test(nint context,uint command,BoundAction* action);
}

/// <summary>Document-scoped bindings. Apply explicitly projects one batch; input actions are copied and polled.</summary>
public sealed unsafe class BoundUiSession<T>:UiSessionOwner
{
    private readonly UiBindings<T>.Binding[] _bindings;
    private readonly UiBindingTarget[] _targets;
    private readonly BoundTarget[] _nativeTargets;
    private readonly BoundValue[] _pending,_sent;
    private readonly BoundRow[] _rows=new BoundRow[64],_sentRows=new BoundRow[64];
    private uint _generation,_revision,_rowCount;
    private bool _applying;
    private UiSourceStaging? _currentStage,_pendingStage;
    private uint _pendingGeneration;
    public UiBindingStatus Status {get{var state=State;return new(state.Generation,state.Generation==_generation?_revision:0,state.Loaded!=0,state.Pending!=0,state.Queued,state.Overflow,UiNative.Text(state.Diagnostic,512));}}
    public uint Revision {get{CheckAccess();return _generation==0||State.Generation!=_generation?0:_revision;}}
    internal UiState State {get{var state=new UiState{Size=(uint)sizeof(UiState)};Native.Check(UiNative.State(Context,&state),"bound UI state");RefreshStaging(state);return state;}}
    private void RefreshStaging(UiState state)
    {
        if(_pendingStage is null||state.Pending!=0)return;
        if(state.Loaded!=0&&state.Generation!=_pendingGeneration){_currentStage?.Dispose();_currentStage=_pendingStage;}
        else _pendingStage.Dispose();
        _pendingStage=null;
    }
    public IReadOnlyList<UiBindingTarget> Targets {get;}
    public BoundUiSession(EngineHost engine,UiBindings<T> bindings):this(engine,Freeze(bindings)){}
    private static UiBindings<T>.Binding[] Freeze(UiBindings<T> bindings){ArgumentNullException.ThrowIfNull(bindings);return bindings.Freeze();}
    // Freeze/validation occurs before acquiring the engine's exclusive UI ownership.
    private BoundUiSession(EngineHost engine,UiBindings<T>.Binding[] bindings):base(engine)
    {
        try{
            _bindings=bindings;_targets=bindings.Select(b=>b.Target).ToArray();Targets=Array.AsReadOnly(_targets);
            _nativeTargets=new BoundTarget[bindings.Length];_pending=new BoundValue[bindings.Length];_sent=new BoundValue[bindings.Length];
            for(int i=0;i<bindings.Length;i++){BoundTarget target=new(){Size=(uint)sizeof(BoundTarget),Kind=(uint)_targets[i].Kind,Action=_targets[i].ActionId};Put(target.Id,48,_targets[i].ElementId);_nativeTargets[i]=target;}
        }catch{Dispose();throw;}
    }
    public void LoadAsset(AssetRoot assets,string logicalPath)
    {
        CheckAccess();var source=BoundUiAuthoring.ValidateAsset(assets,logicalPath,_targets);
        uint generation=_currentStage is not null||_pendingStage is not null?State.Generation:0;
        UiSourceStaging? staging=UiSourceStaging.Create(source);
        byte** paths=stackalloc byte*[UiImageResources.MaximumImages];
        int allocated=0;
        try{
            foreach(var image in source.Images)paths[allocated++]=(byte*)Marshal.StringToCoTaskMemUTF8(image.Path);
            fixed(BoundTarget* targets=_nativeTargets)Native.Check(BoundUiNative.OpenImages(Context,staging.DocumentPath,Environment.GetEnvironmentVariable("GAL_UI_FONT")??"/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc",targets,(uint)_nativeTargets.Length,paths,(uint)source.Images.Count),"bound UI stage");
            MarkNativeOpened();
            _pendingStage?.Dispose();_pendingStage=staging;_pendingGeneration=generation;staging=null;
        }catch{
            // Native open can discard an older pending candidate while preserving the live document.
            if(_pendingStage is not null)_=State;throw;
        }finally{
            for(int i=0;i<allocated;i++)Marshal.FreeCoTaskMem((nint)paths[i]);
            staging?.Dispose();
        }
    }
    internal uint Project(T model)
    {
        ArgumentNullException.ThrowIfNull(model);Array.Clear(_pending);Array.Clear(_rows);uint count=0;
        for(int i=0;i<_bindings.Length;i++){
            var binding=_bindings[i];var target=binding.Target;BoundValue value=new(){Size=(uint)sizeof(BoundValue),Target=(uint)i,Flags=binding.Enabled?.Invoke(model)!=false?1u:0u};
            if(binding.Text is {} text){string result=text(model);UiSettingsContract.ValidateText(result,255,target.Kind==UiBindingKind.TextInput?64:255,target.ElementId);Put(value.Text,256,result);}
            if(binding.Boolean is {} boolean&&boolean(model))value.Flags|=2;
            if(binding.Number is {} number){int result=number(model);if(result is <0 or >100)throw new ArgumentOutOfRangeException(target.ElementId,"UI number must be an integer 0..100.");value.Number=result;}
            if(binding.Rows is {} list){
                var rows=list(model)??throw new ArgumentException("UI list projection cannot return null.");
                int total=rows.Count;
                if(total<0||total>64-count)throw new ArgumentException("At most 64 total UI rows are supported.");
                value.RowFirst=count;value.RowCount=(uint)total;
                for(int r=0;r<total;r++){
                    UiListRow row=rows[r];if(row.Id==0)throw new ArgumentException("UI row ID must be nonzero.");
                    for(uint previous=value.RowFirst;previous<count;previous++)if(_rows[previous].Id==row.Id)throw new ArgumentException("Duplicate UI row ID within list.");
                    UiSettingsContract.ValidateText(row.Text,255,255,target.ElementId);
                    BoundRow copy=new(){Id=row.Id,Flags=(row.Enabled?1u:0u)|(row.Selected?2u:0u)};Put(copy.Text,256,row.Text);_rows[count++]=copy;
                }
            }
            _pending[i]=value;
        }
        return count;
    }
    public bool Apply(T model)
    {
        CheckAccess();if(_applying)throw new InvalidOperationException("UI projection batches cannot re-enter Apply.");
        _applying=true;try{return ApplyCore(model);}finally{_applying=false;}
    }
    private bool ApplyCore(T model)
    {
        CheckAccess();uint count=Project(model);var state=State;
        if(state.Loaded==0||state.Pending!=0)throw new InvalidOperationException("Render the accepted bound UI document before applying its model.");
        bool newDocument=state.Generation!=_generation;
        if(!newDocument&&_revision!=0&&count==_rowCount&&MemoryMarshal.AsBytes(_pending.AsSpan()).SequenceEqual(MemoryMarshal.AsBytes(_sent.AsSpan()))&&MemoryMarshal.AsBytes(_rows.AsSpan(0,(int)count)).SequenceEqual(MemoryMarshal.AsBytes(_sentRows.AsSpan(0,(int)count))))return false;
        if(!newDocument&&_revision==uint.MaxValue)throw new InvalidOperationException("UI model revision exhausted; reload the document.");
        BoundSnapshot snapshot=new(){Size=(uint)sizeof(BoundSnapshot),Version=1,Generation=state.Generation,Revision=newDocument?1:_revision+1,ValueCount=(uint)_pending.Length,RowCount=count};
        fixed(BoundValue* values=_pending)fixed(BoundRow* rows=_rows)Native.Check(BoundUiNative.Apply(Context,&snapshot,values,rows),"bound UI model batch");
        _pending.CopyTo(_sent,0);_rows.CopyTo(_sentRows,0);_generation=state.Generation;_revision=snapshot.Revision;_rowCount=count;return true;
    }
    public UiBindingAction Poll()
    {
        BoundAction action=new(){Size=(uint)sizeof(BoundAction)};Native.Check(BoundUiNative.Poll(Context,&action),"bound UI action");
        return Convert(action);
    }
    public bool IsCurrent(in UiBindingAction action)
    {
        CheckAccess();if(action.IsEmpty||action.Generation!=_generation||action.Revision!=_revision||action.Generation!=State.Generation||(uint)action.Target>=_targets.Length)return false;
        var target=_targets[action.Target];var value=_sent[action.Target];if(target.ActionId!=action.ActionId||target.Kind!=action.Kind||(value.Flags&1)==0)return false;
        if(target.Kind!=UiBindingKind.List)return action.RowId==0;
        for(uint r=value.RowFirst;r<value.RowFirst+value.RowCount;r++)if(_sentRows[r].Id==action.RowId)return (_sentRows[r].Flags&1)!=0;
        return false;
    }
    protected override void OnClosed(){if(_bindings is not null)Array.Clear(_bindings);_generation=_revision=_rowCount=0;_pendingStage?.Dispose();_pendingStage=null;_currentStage?.Dispose();_currentStage=null;}
    public void Capture(string path)=>Native.Check(UiNative.Capture(Context,Path.GetFullPath(path)),"bound UI capture");
    internal UiBindingAction Probe(uint command,int target,ulong row=0,string text="",int number=0,bool boolean=false)
    {
        BoundAction action=new(){Size=(uint)sizeof(BoundAction),Generation=_generation,Revision=_revision,Target=(uint)target,Row=row,Kind=(uint)_targets[target].Kind,Number=number,Flags=boolean?2u:0u};Put(action.Text,256,text);
        Native.Check(BoundUiNative.Test(Context,command,&action),"bound UI probe");return Convert(action);
    }
    private static UiBindingAction Convert(BoundAction a)=>new(a.Generation,a.Revision,(int)a.Target,a.Action,(UiBindingKind)a.Kind,a.Row,UiNative.Text(a.Text,256),(int)a.Number,(a.Flags&2)!=0);
    private static void Put(byte* destination,int capacity,string value){var bytes=new Span<byte>(destination,capacity);bytes.Clear();int length=UiAuthoring.StrictUtf8.GetBytes(value.AsSpan(),bytes);if(length>=capacity)throw new ArgumentException("UI UTF8 payload exceeds capacity.");}
}
