using System.Runtime.InteropServices;
namespace GameAuthoringLab;

/// <summary>Document-scoped, copied generic model and typed command bridge for normal RML/RCSS.</summary>
public sealed unsafe class UiModelSession<T>:UiSessionOwner
{
    private Action<T,UiModelWriter>? _project;
    private readonly ModelSchema[] _schema;private readonly ModelCommand[] _commands;
    private UiCommands.Command[] _registrations = [];
    private readonly string[] _commandNames;
    private readonly UiModelWriter _writer;private readonly ModelValue[] _sent;
    private int _sentCount;private uint _generation,_revision,_pendingGeneration;private bool _applying;
    private UiSourceStaging? _currentStage,_pendingStage;
    private ModelValue[]? _pendingValues;
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
        _registrations=registration.Commands;_project=registration.Project;_schema=registration.Schema;_writer=new(UiModelContract.Capacity(_schema));_sent=new ModelValue[_writer.Values.Length];_commands=new ModelCommand[registration.Commands.Length];_commandNames=new string[_commands.Length];
        for(int i=0;i<_commands.Length;i++){var command=registration.Commands[i];ModelCommand copy=new(){Id=command.Id,Count=(uint)command.Arguments.Length};UiModelContract.Put(copy.Name,command.Name);for(int a=0;a<command.Arguments.Length;a++)copy.Kinds[a]=(uint)command.Arguments[a];_commands[i]=copy;_commandNames[i]=command.Name;}
        } catch { Dispose(); throw; }
    }
    private void EnsureIdle(){CheckAccess();if(_applying)throw new InvalidOperationException("UI projections cannot re-enter session operations.");}
    // Observe publication and retire copied resources only. This never applies a model or draws.
    private UiState State
    {
        get
        {
            var state = new UiState { Size = (uint)sizeof(UiState) };
            NativeCallCount++; Native.Check(UiNative.State(Context, &state), "generic UI state");
            if (_pendingStage is not null && state.Pending == 0)
            {
                if (state.Loaded != 0 && state.Generation != _pendingGeneration)
                {
                    _currentStage?.Dispose(); _currentStage = _pendingStage;
                    if (_pendingValues is { } initial)
                    {
                        initial.CopyTo(_sent, 0); _sentCount = initial.Length;
                        _generation = state.Generation; _revision = 1;
                    }
                }
                else _pendingStage.Dispose();
                _pendingStage = null; _pendingValues = null;
            }
            return state;
        }
    }
    /// <summary>Legacy source-only staging. Draw once to publish, Apply a model, then draw again.
    /// Prefer StageAsset to validate and publish a source together with its initial model.</summary>
    internal void LoadAsset(AssetRoot assets, string logicalPath, IReadOnlyList<string>? declaredImages = null)
    {
        EnsureIdle(); LoadAssetCore(assets, logicalPath, declaredImages, null);
    }
    /// <summary>Copy and validate a candidate document and initial model without drawing.
    /// The next normal engine draw validates its renderer and publishes both at revision 1.
    /// Until publication, Status.Pending is true; Status.Loaded/Revision and commands describe
    /// the previous live document, if any. Apply is rejected while a candidate is pending.
    /// Failed candidates retain the live document, model, resources and commands. A successful
    /// replacement retires its old generation. Check Status after drawing for deferred errors.
    /// Later stages replace a pending candidate; managed validation failures preserve it, while
    /// native staging failures may discard it. Model getters must be pure and cannot re-enter.</summary>
    public void StageAsset(AssetRoot assets, string logicalPath, T initialModel, IReadOnlyList<string>? declaredImages = null)
    {
        EnsureIdle(); _applying = true;
        try
        {
            int count = Project(initialModel);
            // Owned copied values, never a retained mutable model or the reusable Apply writer.
            var initial = _writer.Values.AsSpan(0, count).ToArray();
            LoadAssetCore(assets, logicalPath, declaredImages, initial);
        }
        finally { _applying = false; }
    }
    private void LoadAssetCore(AssetRoot assets, string logicalPath, IReadOnlyList<string>? declaredImages, ModelValue[]? initial)
    {
        var source = UiModelAuthoring.ValidateAsset(assets, logicalPath, _commandNames, declaredImages, _schema, _registrations);
        uint previous = _currentStage is not null || _pendingStage is not null ? State.Generation : 0;
        UiSourceStaging? staging = UiSourceStaging.Create(source);
        byte** paths = stackalloc byte*[UiImageResources.MaximumImages]; int allocated = 0;
        try
        {
            foreach (var image in source.Images) paths[allocated++] = (byte*)Marshal.StringToCoTaskMemUTF8(image.Path);
            string font = Environment.GetEnvironmentVariable("GAL_UI_FONT") ?? "/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc";
            string stylesheet = Path.Combine(staging.DirectoryPath, source.Stylesheet);
            NativeCallCount++;
            fixed (ModelSchema* schema = _schema)
            fixed (ModelCommand* commands = _commands)
            fixed (ModelValue* values = initial)
            {
                if (initial is null)
                    Native.Check(UiModelNative.Open(Context, staging.DocumentPath, font, stylesheet, schema, (uint)_schema.Length,
                        commands, (uint)_commands.Length, paths, (uint)source.Images.Count), "generic UI stage");
                else
                {
                    ModelSnapshot snapshot = new() { Size = (uint)sizeof(ModelSnapshot), Version = 1, Revision = 1, Count = (uint)initial.Length };
                    Native.Check(UiModelNative.Stage(Context, staging.DocumentPath, font, stylesheet, schema, (uint)_schema.Length,
                        commands, (uint)_commands.Length, paths, (uint)source.Images.Count, &snapshot, values), "generic UI initialized stage");
                }
            }
            MarkNativeOpened(); _pendingStage?.Dispose(); _pendingStage = staging; _pendingGeneration = previous;
            _pendingValues = initial; staging = null;
        }
        catch { if (_pendingStage is not null) _ = State; throw; }
        finally { for (int i = 0; i < allocated; i++) Marshal.FreeCoTaskMem((nint)paths[i]); staging?.Dispose(); }
    }
    internal int Project(T model){_writer.Reset();_project!(model,_writer);return _writer.Count;}
    public bool Apply(T model)
    {
        EnsureIdle();_applying=true;try{
            int count=Project(model);var state=State;if(state.Loaded==0||state.Pending!=0)throw new InvalidOperationException("Draw the pending UI candidate before Apply; use StageAsset to supply its initial model.");
            bool changedDocument=state.Generation!=_generation;
            if(!changedDocument&&_revision!=0&&count==_sentCount&&MemoryMarshal.AsBytes(_writer.Values.AsSpan(0,count)).SequenceEqual(MemoryMarshal.AsBytes(_sent.AsSpan(0,count))))return false;
            if(!changedDocument&&_revision==uint.MaxValue)throw new InvalidOperationException("UI revision exhausted; reload the document.");
            ModelSnapshot snapshot=new(){Size=(uint)sizeof(ModelSnapshot),Version=1,Generation=state.Generation,Revision=changedDocument?1:_revision+1,Count=(uint)count};
            fixed(ModelValue* values=_writer.Values){NativeApplyCalls++;NativeCallCount++;Native.Check(UiModelNative.Apply(Context,&snapshot,values),"generic UI snapshot");}
            _writer.Values.AsSpan(0,count).CopyTo(_sent);_sentCount=count;_generation=state.Generation;_revision=snapshot.Revision;return true;
        }finally{_applying=false;}
    }
    public UiCommandEvent Poll(){EnsureIdle();if(_pendingStage is not null)_=State;ModelEvent packet=new(){Size=(uint)sizeof(ModelEvent)};NativeCallCount++;Native.Check(UiModelNative.Poll(Context,&packet),"generic UI poll");return Convert(packet);}
    public bool IsCurrent(in UiCommandEvent packet)
    {
        EnsureIdle();if(packet.IsEmpty||(_currentStage is null&&_pendingStage is null))return false;var state=State;if(packet.Generation!=_generation||packet.Revision!=_revision||packet.Generation!=state.Generation)return false;
        foreach(var command in _commands)if(command.Id==packet.CommandId){if(packet.Count!=command.Count)return false;for(int i=0;i<packet.Count;i++){var arg=packet[i];if((uint)arg.Kind!=command.Kinds[i])return false;if(arg.Kind==UiValueKind.Key){bool found=false;for(int n=0;n<_sentCount;n++)if(_sent[n].Key==arg.Key&&arg.Key!=0){found=true;break;}if(!found)return false;}}return true;}return false;
    }
    /// <summary>Invoke one frozen managed handler only if its packet is still current at this call.
    /// Poll explicitly and drain the current revision before Apply. Returns whether a handler ran,
    /// not whether application state changed; game-rule validation remains in that handler.</summary>
    public bool Dispatch(in UiCommandEvent packet)
    {
        EnsureIdle();
        foreach (var command in _registrations)
            if (command.Id == packet.CommandId && command.Handler is { } handler)
            {
                if (!IsCurrent(packet)) return false;
                handler(packet);
                return true;
            }
        return false;
    }
    public void Capture(string path){EnsureIdle();NativeCallCount++;Native.Check(UiNative.Capture(Context,Path.GetFullPath(path)),"generic UI capture");}
    internal string Probe(uint command,string id,uint occurrence=0,string value="")
    {EnsureIdle();if(_pendingStage is not null)_=State;ModelEvent packet=new(){Size=(uint)sizeof(ModelEvent),Generation=_generation,Revision=_revision};UiSettingsContract.ValidateText(value,255,255,"probe");UiAuthoring.StrictUtf8.GetBytes(value.AsSpan(),new Span<byte>(packet.Arguments[0].Text,256));NativeCallCount++;Native.Check(UiModelNative.Test(Context,command,id,occurrence,&packet),"generic UI probe");return UiNative.Text(packet.Arguments[0].Text,256);}
    private static UiCommandEvent Convert(ModelEvent p)
    {UiCommandArgument Arg(int i){if(i>=p.Count)return default;var a=p.Arguments[i];return new((UiValueKind)a.Kind,UiNative.Text(a.Text,256),a.Number,a.Key);}return new(p.Generation,p.Revision,p.Command,(int)p.Count,Arg(0),Arg(1),Arg(2),Arg(3));}
    protected override void BeforeClose()=>EnsureIdle();
    protected override void OnClosed(){_project=null;_registrations=[];_generation=_revision=0;_sentCount=0;_pendingValues=null;_pendingStage?.Dispose();_pendingStage=null;_currentStage?.Dispose();_currentStage=null;}
}
