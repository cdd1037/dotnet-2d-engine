using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace GameAuthoringLab;

/// <summary>Scalar types accepted by commands. Keys retain all 64 bits and are strings in RmlUi expressions.</summary>
public enum UiValueKind : uint { Text=1, Boolean=2, Number=3, Key=4 }
public readonly record struct UiCommandArgument(UiValueKind Kind,string Text,double Number,ulong Key)
{
    public bool Boolean => Kind==UiValueKind.Boolean ? Number!=0 : throw new InvalidOperationException("Not a Boolean argument.");
}
/// <summary>A copied event. Check IsCurrent immediately before applying application behavior.</summary>
public readonly record struct UiCommandEvent(uint Generation,uint Revision,uint CommandId,int Count,
    UiCommandArgument Argument0,UiCommandArgument Argument1,UiCommandArgument Argument2,UiCommandArgument Argument3)
{
    public bool IsEmpty => CommandId==0;
    public UiCommandArgument this[int index] => index>=0&&index<Count ? index switch {0=>Argument0,1=>Argument1,2=>Argument2,_=>Argument3} : throw new ArgumentOutOfRangeException(nameof(index));
}
/// <summary>Explicit AOT-safe projection; no reflection, runtime generation, or managed callbacks from native UI.</summary>
public abstract class UiData<T>
{
    private protected UiData(){}
    internal abstract Action<T,UiModelWriter> Compile(string name,uint parent,List<ModelSchema> schema,int depth);
}
public static class UiData
{
    public static UiData<string> Text {get;}=new Scalar<string>(UiValueKind.Text,static (w,i,v)=>w.Text(i,v));
    public static UiData<bool> Boolean {get;}=new Scalar<bool>(UiValueKind.Boolean,static (w,i,v)=>w.Values[i].Flags=v?1u:0u);
    public static UiData<double> Number {get;}=new Scalar<double>(UiValueKind.Number,static (w,i,v)=>{if(!double.IsFinite(v)||Math.Abs(v)>9_007_199_254_740_991d)throw new ArgumentOutOfRangeException(nameof(v),"UI numbers must be finite and within ±(2^53-1); use Key for identity.");w.Values[i].Number=v;});
    public static UiData<ulong> Key {get;}=new Scalar<ulong>(UiValueKind.Key,static (w,i,v)=>{if(v==0||!w.Keys.Add(v))throw new ArgumentException("UI keys must be nonzero and unique across the snapshot.");w.Values[i].Key=v;});
    private sealed class Scalar<T>(UiValueKind kind,Action<UiModelWriter,int,T> write):UiData<T>
    {
        internal override Action<T,UiModelWriter> Compile(string name,uint parent,List<ModelSchema> schema,int depth)
        {uint index=UiModelContract.Add(schema,name,parent,(uint)kind,0,depth);return (value,writer)=>write(writer,writer.Add(index,0),value);}
    }
}
/// <summary>A reusable record schema. Sessions freeze fields and delegates at construction.</summary>
public sealed class UiRecord<T> : UiData<T>
{
    private interface IField
    {
        string Name { get; }
        Action<T, UiModelWriter> Compile(uint parent, List<ModelSchema> schema, int depth);
    }
    private sealed record FieldProjection<U>(string Name, Func<T, U> Read, UiData<U> Data, UiDeclaration Origin) : IField
    {
        public Action<T, UiModelWriter> Compile(uint parent, List<ModelSchema> schema, int depth)
        {
            Action<U, UiModelWriter> write;
            try { write = Data.Compile(Name, parent, schema, depth); }
            catch (ArgumentException e) { throw Origin.Error("UI_SCHEMA", Name, "Schema declaration: " + e.Message, e); }
            return (model, writer) =>
            {
                writer.Push(Name, Origin);
                try { write(Read(model), writer); }
                catch (Exception e) when (e is ArgumentException or UiAuthoringException or KeyNotFoundException or IndexOutOfRangeException or NullReferenceException)
                { throw writer.ProjectionError(e); }
                finally { writer.Pop(); }
            };
        }
    }
    private readonly List<IField> _fields = [];
    public UiRecord<T> Field<U>(string name, Func<T, U> read, UiData<U> data,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        var origin = new UiDeclaration(file, line);
        try
        {
            UiModelContract.Name(name); ArgumentNullException.ThrowIfNull(read); ArgumentNullException.ThrowIfNull(data);
            if (_fields.Any(f => f.Name == name)) throw new ArgumentException("Duplicate record field.");
            _fields.Add(new FieldProjection<U>(name, read, data, origin));
            return this;
        }
        catch (ArgumentException e) { throw origin.Error("UI_SCHEMA", name, "Schema declaration: " + e.Message, e); }
    }
    public UiRecord<T> Text(string name, Func<T, string> read, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) => Field(name, read, UiData.Text, file, line);
    public UiRecord<T> Boolean(string name, Func<T, bool> read, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) => Field(name, read, UiData.Boolean, file, line);
    public UiRecord<T> Number(string name, Func<T, double> read, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) => Field(name, read, UiData.Number, file, line);
    public UiRecord<T> Key(string name, Func<T, ulong> read, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) => Field(name, read, UiData.Key, file, line);
    public UiRecord<T> Record<U>(string name, Func<T, U> read, UiRecord<U> data, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) => Field(name, read, data, file, line);
    public UiRecord<T> Array<U>(string name, Func<T, IReadOnlyList<U>> read, UiData<U> element, int maximum = 64,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        try { return Field(name, read, new UiArray<U>(element, maximum), file, line); }
        catch (ArgumentException e) { throw new UiDeclaration(file, line).Error("UI_SCHEMA", name, "Schema declaration: " + e.Message, e); }
    }
    internal override Action<T, UiModelWriter> Compile(string name, uint parent, List<ModelSchema> schema, int depth)
    {
        if (_fields.Count == 0) throw new ArgumentException("Records require at least one field.");
        uint index = UiModelContract.Add(schema, name, parent, 5, 0, depth);
        var fields = _fields.Select(f => f.Compile(index, schema, depth + 1)).ToArray();
        return (model, writer) =>
        {
            ArgumentNullException.ThrowIfNull(model); writer.Add(index, (uint)fields.Length);
            foreach (var field in fields) field(model, writer);
        };
    }
}
/// <summary>Bounded arrays may contain scalars, records, or further arrays.</summary>
public sealed class UiArray<T> : UiData<IReadOnlyList<T>>
{
    private readonly UiData<T> _element;
    private readonly int _maximum;
    public UiArray(UiData<T> element, int maximum = 64)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (maximum is < 1 or > 64) throw new ArgumentOutOfRangeException(nameof(maximum));
        _element = element; _maximum = maximum;
    }
    internal override Action<IReadOnlyList<T>, UiModelWriter> Compile(string name, uint parent, List<ModelSchema> schema, int depth)
    {
        uint index = UiModelContract.Add(schema, name, parent, 6, (uint)_maximum, depth);
        var write = _element.Compile("item", index, schema, depth + 1);
        return (items, writer) =>
        {
            ArgumentNullException.ThrowIfNull(items);
            int n = items.Count;
            if (n < 0 || n > _maximum) throw new ArgumentException("Array exceeds its registered limit.");
            writer.Add(index, (uint)n);
            for (int i = 0; i < n; i++)
            {
                writer.PushIndex(i);
                try { write(items[i], writer); }
                catch (Exception e) when (e is ArgumentException or UiAuthoringException or KeyNotFoundException or IndexOutOfRangeException or NullReferenceException)
                { throw writer.ProjectionError(e); }
                finally { writer.Pop(); }
            }
        };
    }
}
internal static unsafe class UiModelContract
{
    internal static void Name(string name){if(name is not {Length:>=1 and <=47}||name[0] is <'a' or >'z'||name.Any(c=>c is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '_'))throw new ArgumentException("UI names must match [a-z][a-z0-9_]{0,46}.");}
    internal static uint Add(List<ModelSchema> schema,string name,uint parent,uint kind,uint limit,int depth){Name(name);if(schema.Count==128||depth>16)throw new ArgumentException("UI schema exceeds 128 nodes or depth 16 (including array elements).");ModelSchema value=new(){Kind=kind,Parent=parent,Limit=limit};Put(value.Name,name);uint index=(uint)schema.Count;schema.Add(value);return index;}
    internal static int Capacity(ReadOnlySpan<ModelSchema> schema)
    {
        Span<int> counts=stackalloc int[schema.Length];counts.Fill(1);
        for(int i=schema.Length-1;i>0;i--){int parent=(int)schema[i].Parent;int factor=schema[parent].Kind==6?(int)schema[parent].Limit:1;counts[parent]=Math.Min(2048,counts[parent]+counts[i]*factor);}
        return counts[0];
    }
    internal static void Put(byte* destination,string text)=>UiAuthoring.StrictUtf8.GetBytes(text.AsSpan(),new Span<byte>(destination,48));
}
internal sealed unsafe class UiModelWriter
{
    internal readonly ModelValue[] Values;internal UiModelWriter(int capacity=2048)=>Values=new ModelValue[capacity];internal int Count;internal readonly HashSet<ulong> Keys=[];
    private readonly (string? Name, int Index, UiDeclaration? Origin)[] _path = new (string?, int, UiDeclaration?)[16];
    private int _depth;
    internal void Reset(){Count=0;Keys.Clear();_depth=0;}
    internal void Push(string name, UiDeclaration origin) => _path[_depth++] = (name, -1, origin);
    internal void PushIndex(int index) => _path[_depth++] = (null, index, null);
    internal void Pop() => _path[--_depth] = default;
    internal UiAuthoringException ProjectionError(Exception error)
    {
        if (error is UiAuthoringException { Code: "UI_MODEL_VALUE" } existing) return existing;
        var path = new System.Text.StringBuilder("state");
        UiDeclaration origin = default;
        for (int i = 0; i < _depth; i++)
        {
            var part = _path[i];
            if (part.Name is not null) path.Append('.').Append(part.Name);
            else path.Append('[').Append(part.Index).Append(']');
            if (part.Origin is { } declaration) origin = declaration;
        }
        string cause = error is UiAuthoringException authored ? authored.Cause : error.Message;
        return origin.Error("UI_MODEL_VALUE", path.ToString(), "Projection failed at this schema declaration (runtime data source is unknown): " + cause, error);
    }
    internal int Add(uint schema,uint children){if(Count==Values.Length)throw new ArgumentException("UI snapshot exceeds 2048 values.");int index=Count++;Values[index]=new(){Schema=schema,Children=children};return index;}
    internal void Text(int index,string text){UiSettingsContract.ValidateText(text,255,255,"state");fixed(byte* p=Values[index].Text)UiAuthoring.StrictUtf8.GetBytes(text.AsSpan(),new Span<byte>(p,256));}
}
[StructLayout(LayoutKind.Sequential)] internal unsafe struct ModelSchema {public uint Kind,Parent,Limit,Reserved;public fixed byte Name[48];}
[StructLayout(LayoutKind.Sequential)] internal unsafe struct ModelCommand {public uint Id,Count;public fixed uint Kinds[4];public fixed byte Name[48];}
[StructLayout(LayoutKind.Sequential)] internal unsafe struct ModelValue {public uint Schema,Children,Reserved,Flags;public double Number;public ulong Key;public fixed byte Text[256];}
[StructLayout(LayoutKind.Sequential)] internal struct ModelSnapshot {public uint Size,Version,Generation,Revision,Count,Reserved;}
[StructLayout(LayoutKind.Sequential)] internal unsafe struct ModelArgument {public uint Kind,Reserved;public double Number;public ulong Key;public fixed byte Text[256];}
[InlineArray(4)] internal struct ModelArguments {private ModelArgument _first;}
[StructLayout(LayoutKind.Sequential)] internal struct ModelEvent {public uint Size,Generation,Revision,Command,Count,Reserved;public ModelArguments Arguments;}
internal static unsafe partial class UiModelNative
{
    [LibraryImport("gal",EntryPoint="gal_ui_model_open",StringMarshalling=StringMarshalling.Utf8)] [UnmanagedCallConv(CallConvs=[typeof(CallConvCdecl)])] internal static partial int Open(nint context,string path,string font,string stylesheet,ModelSchema* schema,uint schemaCount,ModelCommand* commands,uint commandCount,byte** images,uint imageCount);
    [LibraryImport("gal",EntryPoint="gal_ui_model_apply")] [UnmanagedCallConv(CallConvs=[typeof(CallConvCdecl)])] internal static partial int Apply(nint context,ModelSnapshot* snapshot,ModelValue* values);
    [LibraryImport("gal",EntryPoint="gal_ui_model_poll")] [UnmanagedCallConv(CallConvs=[typeof(CallConvCdecl)])] internal static partial int Poll(nint context,ModelEvent* packet);
    [LibraryImport("gal",EntryPoint="gal_ui_model_test",StringMarshalling=StringMarshalling.Utf8)] [UnmanagedCallConv(CallConvs=[typeof(CallConvCdecl)])] internal static partial int Test(nint context,uint command,string id,uint occurrence,ModelEvent* packet);
}
