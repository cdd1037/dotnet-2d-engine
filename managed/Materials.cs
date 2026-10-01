using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace GameAuthoringLab;

/// <summary>Copied fragment constants. Their meaning belongs to the material shader, not the engine.</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct MaterialParameters
{
    public readonly Vector4 First,Second;
    public MaterialParameters(Vector4 first,Vector4 second=default)
    {
        Validate(first);Validate(second);First=first;Second=second;
    }
    private static void Validate(Vector4 value)
    {
        if(!float.IsFinite(value.X)||!float.IsFinite(value.Y)||!float.IsFinite(value.Z)||!float.IsFinite(value.W))
            throw new ArgumentOutOfRangeException(nameof(value),"Material parameters must be finite.");
    }
}

/// <summary>Additive material draw ABI. Handle zero preserves the default pipeline and requires zero parameters.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct MaterialDraw
{
    public uint Size,Version;
    public SpriteDrawV2 Sprite;
    public ulong Material;
    public MaterialParameters Parameters;
    public static unsafe MaterialDraw Create(SpriteDrawV2 sprite,ulong material=0,MaterialParameters parameters=default)
        =>new(){Size=(uint)sizeof(MaterialDraw),Version=1,Sprite=sprite,Material=material,Parameters=parameters};
}

public readonly record struct MaterialInfo(string Path,string FragmentPath,string Profile,string Sha256);

internal sealed class MaterialDocument
{
    public required int Version { get; init; }
    public required string Profile { get; init; }
    public required string Fragment { get; init; }
    public required string Sha256 { get; init; }
    public required int ParameterBytes { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,AllowDuplicateProperties=false,RespectNullableAnnotations=true,GenerationMode=JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(MaterialDocument))]
internal partial class MaterialJsonContext : JsonSerializerContext;

internal static class MaterialAsset
{
    internal const string Profile="sprite-fragment-v1";
    internal const int MaximumShaderBytes=256*1024;
    internal static (MaterialInfo Info,byte[] Code) Load(AssetRoot assets,string logicalPath)
    {
        try
        {
            var manifest=ReadBounded(assets,logicalPath,16384);
            var document=JsonSerializer.Deserialize(manifest,MaterialJsonContext.Default.MaterialDocument)
                ??throw new JsonException("Expected a material object.");
            if(document.Version!=1||document.Profile!=Profile||document.ParameterBytes!=32)
                throw Error("MATERIAL_PROFILE","Expected version 1, sprite-fragment-v1 and exactly 32 parameter bytes.");
            if(document.Sha256.Length!=64||document.Sha256.Any(c=>!(c is >= '0' and <= '9' or >= 'a' and <= 'f')))
                throw Error("MATERIAL_HASH","Expected a lowercase SHA-256 digest.");
            string fragment=assets.Sibling(logicalPath,document.Fragment);
            if(!fragment.EndsWith(".spv",StringComparison.Ordinal))throw Error("MATERIAL_SHADER","Expected an offline-compiled .spv fragment asset.");
            byte[] code=ReadBounded(assets,fragment,MaximumShaderBytes);
            if(Convert.ToHexStringLower(SHA256.HashData(code))!=document.Sha256)
                throw Error("MATERIAL_HASH","Fragment hash differs from manifest; rebuild this shader asset.");
            return (new(assets.FilePath(logicalPath),fragment,document.Profile,document.Sha256),code);
        }
        catch(AssetException){throw;}
        catch(Exception e) when(e is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
        {throw new AssetException("MATERIAL_SOURCE",assets.DirectoryPath,logicalPath,e.Message,e);}
        AssetException Error(string code,string why)=>new(code,assets.DirectoryPath,logicalPath,why);
    }
    private static byte[] ReadBounded(AssetRoot assets,string logicalPath,int maximum)
    {
        using var stream=File.OpenRead(assets.Resolve(logicalPath));
        if(stream.Length<1||stream.Length>maximum)throw new AssetException("MATERIAL_SIZE",assets.DirectoryPath,logicalPath,$"Expected 1..{maximum} bytes.");
        byte[] bytes=new byte[(int)stream.Length];stream.ReadExactly(bytes);
        if(stream.ReadByte()!=-1)throw new IOException("Material source changed while reading.");return bytes;
    }
}

/// <summary>Synchronous snapshot cache of trusted, offline-prepared fragment shader assets. No implicit reload.</summary>
public sealed unsafe class MaterialCache : IEngineOwned
{
    public const int MaximumMaterials=64;
    private sealed class Entry(ulong handle,MaterialInfo info){public readonly ulong Handle=handle;public readonly MaterialInfo Info=info;public int References=1;}
    private readonly EngineHost _engine;
    private readonly Dictionary<string,Entry> _entries=new(StringComparer.Ordinal);
    private bool _destroyed;
    internal MaterialCache(EngineHost engine)=>_engine=engine;
    public int Count{get{CheckAccess();return _entries.Count;}}
    public int Loads{get;private set;}
    public int Releases{get;private set;}
    internal void CheckAccess(){_engine.AssertAlive();ObjectDisposedException.ThrowIf(_destroyed,this);}
    public MaterialLease Acquire(AssetRoot assets,string logicalPath)
    {
        CheckAccess();ArgumentNullException.ThrowIfNull(assets);string path=assets.FilePath(logicalPath);
        if(_entries.TryGetValue(path,out var existing))
        {
            if(existing.References==int.MaxValue)throw new InvalidOperationException("Material lease count exhausted.");
            var lease=new MaterialLease(this,path,existing.Handle,existing.Info);existing.References++;return lease;
        }
        if(_entries.Count>=MaximumMaterials)throw new AssetException("MATERIAL_CAPACITY",assets.DirectoryPath,logicalPath,"At most 64 distinct cached materials per engine.");
        var asset=MaterialAsset.Load(assets,logicalPath);ulong handle=0;
        var descriptor=new MaterialDescriptor{Size=(uint)sizeof(MaterialDescriptor),Version=1,FragmentBytes=(uint)asset.Code.Length,ParameterBytes=32};
        try{fixed(byte* code=asset.Code)Native.Check(MaterialNative.Create(_engine.NativeContext,&descriptor,code,&handle),"create material");}
        catch(InvalidOperationException e){throw new AssetException("MATERIAL_PIPELINE",assets.DirectoryPath,logicalPath,e.Message,e);}
        try{var lease=new MaterialLease(this,path,handle,asset.Info);_entries.Add(path,new(handle,asset.Info));Loads++;return lease;}
        catch{MaterialNative.Release(_engine.NativeContext,handle);throw;}
    }
    internal void Release(string path)
    {
        _engine.AssertThread();if(_destroyed)return;CheckAccess();var entry=_entries[path];
        if(entry.References>1){entry.References--;return;}
        Native.Check(MaterialNative.Release(_engine.NativeContext,entry.Handle),"release material");_entries.Remove(path);Releases++;
    }
    void IEngineOwned.EngineDestroyed(){Releases+=_entries.Count;_entries.Clear();_destroyed=true;}
}

public sealed class MaterialLease : IDisposable
{
    private readonly MaterialCache _cache;
    private readonly string _path;
    private readonly ulong _handle;
    private readonly MaterialInfo _info;
    private bool _disposed;
    internal MaterialLease(MaterialCache cache,string path,ulong handle,MaterialInfo info){_cache=cache;_path=path;_handle=handle;_info=info;}
    public ulong Handle{get{ObjectDisposedException.ThrowIf(_disposed,this);_cache.CheckAccess();return _handle;}}
    public MaterialInfo Info{get{ObjectDisposedException.ThrowIf(_disposed,this);_cache.CheckAccess();return _info;}}
    public void Dispose(){if(_disposed)return;_cache.Release(_path);_disposed=true;}
}

[StructLayout(LayoutKind.Sequential)]
internal struct MaterialDescriptor{public uint Size,Version,FragmentBytes,ParameterBytes;}
internal static unsafe partial class MaterialNative
{
    [LibraryImport("gal",EntryPoint="gal_material_create_v1")][UnmanagedCallConv(CallConvs=[typeof(CallConvCdecl)])]
    internal static partial int Create(nint context,MaterialDescriptor* descriptor,byte* fragment,ulong* material);
    [LibraryImport("gal",EntryPoint="gal_material_release")][UnmanagedCallConv(CallConvs=[typeof(CallConvCdecl)])]
    internal static partial int Release(nint context,ulong material);
    [LibraryImport("gal",EntryPoint="gal_material_count")][UnmanagedCallConv(CallConvs=[typeof(CallConvCdecl)])]
    internal static partial int Count(nint context,uint* count);
    [LibraryImport("gal",EntryPoint="gal_submit_material_draws_v1")][UnmanagedCallConv(CallConvs=[typeof(CallConvCdecl)])]
    internal static partial int Submit(nint context,MaterialDraw* draws,uint count,FramebufferClip* clips,uint clipCount);
}
