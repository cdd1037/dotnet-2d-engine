using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace GameAuthoringLab;

public sealed class TileMapDocument
{
    public required string Kind { get; init; }
    public required int Version { get; init; }
    public required string Name { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required float TileWidth { get; init; }
    public required float TileHeight { get; init; }
    public required List<AuthoredResource> Resources { get; init; }
    public required List<TileDefinitionRecord> Tiles { get; init; }
    public required List<TileLayerRecord> Layers { get; init; }
}
public sealed class TileDefinitionRecord
{
    public required int Id { get; init; }
    public required string AssetKey { get; init; }
    public bool Solid { get; init; }
}
public sealed class TileLayerRecord
{
    public required string Name { get; init; }
    public required int Order { get; init; }
    // A settable DTO property preserves the initializer when sourcegen sees no JSON member.
    public float Opacity { get; set; } = 1;
    public required int[] Cells { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int[]? Flips { get; init; }
}
public readonly record struct TileDefinition(string AssetKey, bool Solid);
public sealed class TileLayer
{
    private readonly int[] _cells;
    private readonly byte[] _flips;
    private readonly bool[] _chunks;
    public string Name { get; }
    public int Order { get; }
    public float Opacity { get; }
    internal TileLayer(TileLayerRecord source, int width, int height)
    {
        Name = source.Name; Order = source.Order; Opacity = source.Opacity;
        _cells = (int[])source.Cells.Clone(); _flips = source.Flips is null ? new byte[_cells.Length] : Array.ConvertAll(source.Flips,static value=>(byte)value);
        int chunksWide = (width + TileMap.ChunkSize - 1) / TileMap.ChunkSize;
        _chunks = new bool[chunksWide * ((height + TileMap.ChunkSize - 1) / TileMap.ChunkSize)];
        for (int y=0;y<height;y++) for (int x=0;x<width;x++)
            if (_cells[y*width+x] != 0) _chunks[(y/TileMap.ChunkSize)*chunksWide+x/TileMap.ChunkSize] = true;
    }
    public int Cell(int index) => _cells[index];
    public byte Flip(int index) => _flips[index];
    internal bool ChunkOccupied(int index) => _chunks[index];
}
/// <summary>Copied immutable orthogonal grid. Runtime chunks and native handles never enter the source document.</summary>
public sealed class TileMap
{
    public const int ChunkSize = 16, MaximumDimension = 256, MaximumCellSlots = 131072, MaximumLayers = 8, MaximumTiles = 256;
    private readonly TileDefinition[] _tiles;
    private readonly TileLayer[] _layers;
    private readonly string[] _assetKeys;
    public string Name { get; }
    public int Width { get; }
    public int Height { get; }
    public float TileWidth { get; }
    public float TileHeight { get; }
    public ReadOnlySpan<TileLayer> Layers => _layers;
    public ReadOnlySpan<string> AssetKeys => _assetKeys;
    public TileDefinition Tile(int id) => id > 0 && id < _tiles.Length && _tiles[id].AssetKey is not null ? _tiles[id] : throw new ArgumentOutOfRangeException(nameof(id));
    internal TileMap(TileMapDocument source)
    {
        Name = source.Name; Width = source.Width; Height = source.Height; TileWidth = source.TileWidth; TileHeight = source.TileHeight;
        _tiles = new TileDefinition[1025];
        foreach (var tile in source.Tiles) _tiles[tile.Id] = new(tile.AssetKey,tile.Solid);
        // Explicit source index tie-breaker, independent of sorting implementation stability.
        _layers = source.Layers.Select((layer,index)=>(Layer:new TileLayer(layer,Width,Height),Index:index))
            .OrderBy(pair=>pair.Layer.Order).ThenBy(pair=>pair.Index).Select(pair=>pair.Layer).ToArray();
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var layer in _layers) for(int i=0;i<Width*Height;i++) if(layer.Cell(i)!=0) used.Add(_tiles[layer.Cell(i)].AssetKey);
        _assetKeys = used.Order(StringComparer.Ordinal).ToArray();
    }
}
public sealed class LoadedTileMap
{
    public TileMapDocument Source {get;}
    public TileMap Map {get;}
    public AssetCatalog Catalog {get;}
    internal LoadedTileMap(TileMapDocument source,TileMap map,AssetCatalog catalog){Source=source;Map=map;Catalog=catalog;}
}
public sealed class TileMapException(string code,string source,string path,string message,Exception? inner=null)
    : Exception($"{source} [{code}] {path}: {message}",inner)
{
    public string Code { get; } = code;
    public string SourcePath { get; } = source;
    public string JsonPath { get; } = path;
}
public static class TileMapAsset
{
    public const int MaximumBytes = 2*1024*1024;
    private static readonly UTF8Encoding Utf8 = new(false,true);
    public static LoadedTileMap LoadAsset(AssetRoot assets,string logicalPath)
    {
        ArgumentNullException.ThrowIfNull(assets);
        try
        {
            string path = assets.Resolve(logicalPath); using var stream = File.OpenRead(path);
            if(stream.Length is <1 or >MaximumBytes) throw Error("TILE_SIZE",path,"$","Expected 1..2097152 UTF-8 bytes.");
            var bytes = new byte[(int)stream.Length]; stream.ReadExactly(bytes);
            if(stream.ReadByte()!=-1) throw Error("TILE_SIZE",path,"$","Source changed while reading.");
            return Load(Utf8.GetString(bytes),path,assets);
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or DecoderFallbackException or AssetException)
        { throw Error("TILE_FILE",logicalPath,"$",e.Message,e); }
    }
    public static LoadedTileMap Load(string json,string sourcePath,AssetRoot assets)
    {
        ArgumentNullException.ThrowIfNull(assets);ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        CheckBytes(json,sourcePath);
        TileMapDocument source;
        try { source = JsonSerializer.Deserialize(json,TileMapJsonContext.Default.TileMapDocument) ?? throw Error("TILE_JSON",sourcePath,"$","Document cannot be null."); }
        catch(JsonException e) { throw Error("TILE_JSON",sourcePath,e.Path??"$",$"Line {(e.LineNumber??0)+1}, byte column {(e.BytePositionInLine??0)+1}: {e.Message}",e); }
        return Build(source,sourcePath,assets);
    }
    public static string Write(TileMapDocument source,string sourcePath,AssetRoot assets)
    { _ = Build(source,sourcePath,assets); string json = JsonSerializer.Serialize(source,TileMapJsonContext.Default.TileMapDocument); CheckBytes(json,sourcePath); return json; }
    private static void CheckBytes(string json,string source)
    {
        ArgumentNullException.ThrowIfNull(json);
        try { if(json.Length>MaximumBytes || Utf8.GetByteCount(json)>MaximumBytes) throw Error("TILE_SIZE",source,"$","Source exceeds 2097152 UTF-8 bytes."); }
        catch(EncoderFallbackException e) { throw Error("TILE_JSON",source,"$","Invalid Unicode text.",e); }
    }
    public static LoadedTileMap Build(TileMapDocument source,string file,AssetRoot assets)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(assets);ArgumentException.ThrowIfNullOrWhiteSpace(file);
        if(source.Kind!="gal-tilemap" || source.Version!=1) throw Error("TILE_VERSION",file,"$.kind/version","Expected gal-tilemap version 1.");
        if(string.IsNullOrWhiteSpace(source.Name) || source.Name.Length>128) throw Error("TILE_NAME",file,"$.name","Expected 1..128 nonblank characters.");
        if(source.Width is <1 or >TileMap.MaximumDimension || source.Height is <1 or >TileMap.MaximumDimension) throw Error("TILE_LIMIT",file,"$.width/height","Grid dimensions must be in 1..256.");
        if(!float.IsFinite(source.TileWidth)||!float.IsFinite(source.TileHeight)||source.TileWidth is <1 or >4096||source.TileHeight is <1 or >4096) throw Error("TILE_SIZE",file,"$.tileWidth/tileHeight","Tile pixel extents must be finite and in 1..4096.");
        if(source.Layers is null || source.Layers.Count is <1 or >TileMap.MaximumLayers || (long)source.Width*source.Height*source.Layers.Count>TileMap.MaximumCellSlots) throw Error("TILE_LIMIT",file,"$.layers","Expected 1..8 layers and at most 131072 total cell slots.");
        if(source.Tiles is null || source.Tiles.Count is <1 or >TileMap.MaximumTiles) throw Error("TILE_LIMIT",file,"$.tiles","Expected 1..256 tile definitions.");
        if(source.Resources is null || source.Resources.Count is <1 or >256) throw Error("TILE_LIMIT",file,"$.resources","Expected 1..256 resources.");
        var resources = new Dictionary<string,TextureAsset>(StringComparer.Ordinal);
        string logicalSource;
        try { logicalSource = assets.LogicalPathFor(file); }
        catch(AssetException e) { throw Error("TILE_RESOURCE",file,"$",e.Message,e); }
        for(int i=0;i<source.Resources.Count;i++)
        {
            var r = source.Resources[i]; string p = $"$.resources[{i}]";
            if(r is null || string.IsNullOrWhiteSpace(r.Key) || r.Key.Length>128 || resources.ContainsKey(r.Key)) throw Error("TILE_RESOURCE",file,p+".key","Expected unique nonempty resource key up to 128 characters.");
            string path; BitmapInfo info;
            try { path = assets.Sibling(logicalSource,r.Path); info = assets.ReadImageInfo(path); }
            catch(AssetException e) { throw Error("TILE_RESOURCE",file,p+".path",e.Message,e); }
            try { r.Region?.ToRegion().Validate(info.Width,info.Height); }
            catch(ArgumentOutOfRangeException e) { throw Error("TILE_RESOURCE",file,p+".region",e.Message,e); }
            resources.Add(r.Key,new(path,r.Region?.ToRegion()));
        }
        var ids = new HashSet<int>();
        for(int i=0;i<source.Tiles.Count;i++)
        {
            var tile = source.Tiles[i]; string p = $"$.tiles[{i}]";
            if(tile is null || tile.Id is <1 or >1024 || !ids.Add(tile.Id)) throw Error("TILE_ID",file,p+".id","Expected unique tile ID in 1..1024; zero is empty.");
            if(tile.AssetKey is null || !resources.ContainsKey(tile.AssetKey)) throw Error("TILE_RESOURCE",file,p+".assetKey","Unregistered resource key.");
        }
        var names = new HashSet<string>(StringComparer.Ordinal); int cells = source.Width*source.Height;
        for(int i=0;i<source.Layers.Count;i++)
        {
            var layer = source.Layers[i]; string p = $"$.layers[{i}]";
            if(layer is null || string.IsNullOrWhiteSpace(layer.Name) || layer.Name.Length>128 || !names.Add(layer.Name)) throw Error("TILE_NAME",file,p+".name","Expected unique nonblank layer name up to 128 characters.");
            if(!float.IsFinite(layer.Opacity)||layer.Opacity is <0 or >1) throw Error("TILE_VALUE",file,p+".opacity","Expected opacity in [0,1].");
            if(layer.Cells is null || layer.Cells.Length!=cells) throw Error("TILE_CELLS",file,p+".cells","Expected exactly width*height row-major cells.");
            if(layer.Flips is not null && layer.Flips.Length!=cells) throw Error("TILE_CELLS",file,p+".flips","Expected one flag byte per cell when flips are provided.");
            for(int j=0;j<cells;j++)
            {
                if(layer.Cells[j]!=0 && !ids.Contains(layer.Cells[j])) throw Error("TILE_ID",file,$"{p}.cells[{j}]","Cell references an unregistered tile ID.");
                if(layer.Flips is not null && (layer.Flips[j] is <0 or >3 || (layer.Cells[j]==0 && layer.Flips[j]!=0))) throw Error("TILE_VALUE",file,$"{p}.flips[{j}]","Only X=1/Y=2 flip bits on nonempty cells are supported.");
            }
        }
        return new(source,new TileMap(source),new AssetCatalog(assets,resources));
    }
    private static TileMapException Error(string code,string file,string path,string message,Exception? inner=null) => new(code,file,path,message,inner);
}
[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,AllowDuplicateProperties=false,RespectNullableAnnotations=true,GenerationMode=JsonSourceGenerationMode.Metadata,WriteIndented=true)]
[JsonSerializable(typeof(TileMapDocument))]
internal partial class TileMapJsonContext : JsonSerializerContext;
