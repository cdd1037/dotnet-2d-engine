using System.Runtime.InteropServices;
namespace GameAuthoringLab;

/// <summary>Document-scoped, copied generic model and typed command bridge for normal RML/RCSS.</summary>
public sealed unsafe class UiModelSession<T>:UiSessionOwner
{
    private Action<T,UiModelWriter>? _project;
    private readonly ModelSchema[] _schema;private readonly ModelCommand[] _commands;
    private readonly string[] _commandNames;
    private readonly UiModelWriter _writer;private readonly ModelValue[] _sent;
    private int _sentCount;private uint _generation,_revision,_pendingGeneration;private bool _applying;
    private UiSourceStaging? _currentStage,_pendingStage;
    public ulong NativeCallCount {get;private set;}
    public ulong NativeApplyCalls {get;private set;}
    public UiBindingStatus Status {get{EnsureIdle();var s=State;return new(s.Generation,s.Generation==_generation?_revision:0,s.Loaded!=0,s.Pending!=0,s.Queued,s.Overflow,UiNative.Text(s.Diagnostic,512));}}
    public uint Revision=>Status.Revision;
    private sealed record Registration(Action<T,UiModelWriter> Project,ModelSchema[] Schema,UiCommands.Command[] Commands);
    private static Registration Freeze(UiRecord<T> schema,UiCommands? commands){ArgumentNullException.ThrowIfNull(schema);List<ModelSchema> nodes=[];var project=schema.Compile("state",uint.MaxValue,nodes,1);return new(project,nodes.ToArray(),commands?.Freeze()??[]);}
    public UiModelSession(EngineHost engine,UiRecord<T> schema,UiCommands? commands=null):this(engine,Freeze(schema,commands)){}
    private UiModelSession(EngineHost engine,Registration registration):base(engine)
    {
        try {
        _project=registration.Project;_schema=registration.Schema;_writer=new(UiModelContract.Capacity(_schema));_sent=new ModelValue[_writer.Values.Length];_commands=new ModelCommand[registration.Commands.Length];_commandNames=new string[_commands.Length];
        for(int i=0;i<_commands.Length;i++){var command=registration.Commands[i];ModelCommand copy=new(){Id=command.Id,Count=(uint)command.Arguments.Length};UiModelContract.Put(copy.Name,command.Name);for(int a=0;a<command.Arguments.Length;a++)copy.Kinds[a]=(uint)command.Arguments[a];_commands[i]=copy;_commandNames[i]=command.Name;}
        } catch { Dispose(); throw; }
    }
    private void EnsureIdle(){CheckAccess();if(_applying)throw new InvalidOperationException("UI projections cannot re-enter session operations.");}
    private UiState State {get{var s=new UiState{Size=(uint)sizeof(UiState)};NativeCallCount++;Native.Check(UiNative.State(Context,&s),"generic UI state");if(_pendingStage is not null&&s.Pending==0){if(s.Loaded!=0&&s.Generation!=_pendingGeneration){_currentStage?.Dispose();_currentStage=_pendingStage;}else _pendingStage.Dispose();_pendingStage=null;}return s;}}
    public void LoadAsset(AssetRoot assets,string logicalPath,IReadOnlyList<string>? declaredImages=null)
    {
        EnsureIdle();var source=UiModelAuthoring.ValidateAsset(assets,logicalPath,_commandNames,declaredImages);uint previous=_currentStage is not null||_pendingStage is not null?State.Generation:0;
        UiSourceStaging? staging=UiSourceStaging.Create(source);byte** paths=stackalloc byte*[UiImageResources.MaximumImages];int allocated=0;
        try{foreach(var image in source.Images)paths[allocated++]=(byte*)Marshal.StringToCoTaskMemUTF8(image.Path);
            NativeCallCount++;fixed(ModelSchema* schema=_schema)fixed(ModelCommand* commands=_commands)Native.Check(UiModelNative.Open(Context,staging.DocumentPath,Environment.GetEnvironmentVariable("GAL_UI_FONT")??"/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc",Path.Combine(staging.DirectoryPath,source.Stylesheet),schema,(uint)_schema.Length,commands,(uint)_commands.Length,paths,(uint)source.Images.Count),"generic UI stage");
            MarkNativeOpened();_pendingStage?.Dispose();_pendingStage=staging;_pendingGeneration=previous;staging=null;
        }catch{if(_pendingStage is not null)_=State;throw;}
        finally{for(int i=0;i<allocated;i++)Marshal.FreeCoTaskMem((nint)paths[i]);staging?.Dispose();}
    }
    internal int Project(T model){_writer.Reset();_project!(model,_writer);return _writer.Count;}
    public bool Apply(T model)
    {
        EnsureIdle();_applying=true;try{
            int count=Project(model);var state=State;if(state.Loaded==0||state.Pending!=0)throw new InvalidOperationException("Render the accepted UI document before applying its model.");
            bool changedDocument=state.Generation!=_generation;
            if(!changedDocument&&_revision!=0&&count==_sentCount&&MemoryMarshal.AsBytes(_writer.Values.AsSpan(0,count)).SequenceEqual(MemoryMarshal.AsBytes(_sent.AsSpan(0,count))))return false;
            if(!changedDocument&&_revision==uint.MaxValue)throw new InvalidOperationException("UI revision exhausted; reload the document.");
            ModelSnapshot snapshot=new(){Size=(uint)sizeof(ModelSnapshot),Version=1,Generation=state.Generation,Revision=changedDocument?1:_revision+1,Count=(uint)count};
            fixed(ModelValue* values=_writer.Values){NativeApplyCalls++;NativeCallCount++;Native.Check(UiModelNative.Apply(Context,&snapshot,values),"generic UI snapshot");}
            _writer.Values.AsSpan(0,count).CopyTo(_sent);_sentCount=count;_generation=state.Generation;_revision=snapshot.Revision;return true;
        }finally{_applying=false;}
    }
    public UiCommandEvent Poll(){EnsureIdle();ModelEvent packet=new(){Size=(uint)sizeof(ModelEvent)};NativeCallCount++;Native.Check(UiModelNative.Poll(Context,&packet),"generic UI poll");return Convert(packet);}
    public bool IsCurrent(in UiCommandEvent packet)
    {
        EnsureIdle();if(packet.IsEmpty||packet.Generation!=_generation||packet.Revision!=_revision||packet.Generation!=State.Generation)return false;
        foreach(var command in _commands)if(command.Id==packet.CommandId){if(packet.Count!=command.Count)return false;for(int i=0;i<packet.Count;i++){var arg=packet[i];if((uint)arg.Kind!=command.Kinds[i])return false;if(arg.Kind==UiValueKind.Key){bool found=false;for(int n=0;n<_sentCount;n++)if(_sent[n].Key==arg.Key&&arg.Key!=0){found=true;break;}if(!found)return false;}}return true;}return false;
    }
    public void Capture(string path){EnsureIdle();NativeCallCount++;Native.Check(UiNative.Capture(Context,Path.GetFullPath(path)),"generic UI capture");}
    internal string Probe(uint command,string id,uint occurrence=0,string value="")
    {EnsureIdle();ModelEvent packet=new(){Size=(uint)sizeof(ModelEvent),Generation=_generation,Revision=_revision};UiSettingsContract.ValidateText(value,255,255,"probe");UiAuthoring.StrictUtf8.GetBytes(value.AsSpan(),new Span<byte>(packet.Arguments[0].Text,256));NativeCallCount++;Native.Check(UiModelNative.Test(Context,command,id,occurrence,&packet),"generic UI probe");return UiNative.Text(packet.Arguments[0].Text,256);}
    private static UiCommandEvent Convert(ModelEvent p)
    {UiCommandArgument Arg(int i){if(i>=p.Count)return default;var a=p.Arguments[i];return new((UiValueKind)a.Kind,UiNative.Text(a.Text,256),a.Number,a.Key);}return new(p.Generation,p.Revision,p.Command,(int)p.Count,Arg(0),Arg(1),Arg(2),Arg(3));}
    protected override void BeforeClose()=>EnsureIdle();
    protected override void OnClosed(){_project=null;_generation=_revision=0;_sentCount=0;_pendingStage?.Dispose();_pendingStage=null;_currentStage?.Dispose();_currentStage=null;}
}
