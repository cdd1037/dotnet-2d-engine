using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameAuthoringLab;

// Source assets, deliberately distinct from WorldSaveDocument and game progress.
// Array order is meaningful: equal-layer sprites retain this order.
public sealed class AuthoredSceneDocument
{
    public required string Kind { get; init; }
    public required int Version { get; init; }
    public required Guid Id { get; init; }
    public required Guid PersistentScopeId { get; init; }
    public required string Name { get; init; }
    public required List<AuthoredResource> Resources { get; init; }
    public required List<EntityRecord> Entities { get; init; }
}
public sealed class AuthoredResource
{
    public required string Key { get; init; }
    public required string Path { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RegionRecord? Region { get; init; }
}
public sealed class RegionRecord
{
    public required int X { get; init; }
    public required int Y { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    internal TextureRegion ToRegion() => new(X, Y, Width, Height);
}
public sealed class AuthoredSceneException(string code, string file, string path, Guid? entityId, string cause,
    Exception? inner = null) : Exception($"{file} [{code}] {path}" + (entityId is {} id ? $" (entity {id})" : "") + ": " + cause, inner)
{
    public string Code { get; } = code;
    public string FilePath { get; } = file;
    public string JsonPath { get; } = path;
    public Guid? EntityId { get; } = entityId;
}
public sealed record LoadedAuthoredScene(AuthoredSceneDocument Source, World World, IReadOnlyDictionary<string, string> Resources, AssetCatalog Catalog);

public static class AuthoredScene
{
    public const string Kind = "gal-authored-scene";
    private const int MaximumBytes = 1024 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static LoadedAuthoredScene LoadAsset(AssetRoot assets, string logicalPath)
        => LoadFile(assets.Resolve(logicalPath), assets);

    public static LoadedAuthoredScene LoadFile(string path, AssetRoot? assets = null)
    {
        string full = System.IO.Path.GetFullPath(path);
        try
        {
            if (assets is not null) full = assets.Resolve(assets.LogicalPathFor(full));
            using var stream = File.OpenRead(full);
            if (stream.Length is <= 0 or > MaximumBytes) throw Error("SCENE_SIZE", full, "$", null, "Expected 1..1048576 UTF-8 bytes.");
            byte[] bytes = new byte[(int)stream.Length];
            stream.ReadExactly(bytes);
            if (stream.ReadByte() != -1) throw Error("SCENE_SIZE", full, "$", null, "Source changed while reading.");
            return Load(Utf8.GetString(bytes), full, assets);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or DecoderFallbackException)
        { throw Error("SCENE_FILE", full, "$", null, e.Message, e); }
    }

    public static LoadedAuthoredScene Load(string json, string sourcePath, AssetRoot? assets = null)
    {
        if (json.Length > MaximumBytes || Utf8.GetByteCount(json) > MaximumBytes)
            throw Error("SCENE_SIZE", sourcePath, "$", null, "Source exceeds 1048576 UTF-8 bytes.");
        AuthoredSceneDocument doc;
        try
        {
            doc = JsonSerializer.Deserialize(json, AuthoredSceneJsonContext.Default.AuthoredSceneDocument)
                ?? throw Error("SCENE_JSON", sourcePath, "$", null, "Document cannot be null.");
        }
        catch (JsonException e)
        { throw Error("SCENE_JSON", sourcePath, e.Path ?? "$", null, $"Line {(e.LineNumber ?? 0) + 1}, byte column {(e.BytePositionInLine ?? 0) + 1}: {e.Message}", e); }
        return Build(doc, sourcePath, assets);
    }

    public static string Write(AuthoredSceneDocument document, string sourcePath, AssetRoot? assets = null)
    {
        _ = Build(document, sourcePath, assets); // Same validation path used by runtime and tools.
        string json = JsonSerializer.Serialize(document, AuthoredSceneJsonContext.Default.AuthoredSceneDocument);
        if (Utf8.GetByteCount(json) > MaximumBytes) throw Error("SCENE_SIZE", sourcePath, "$", null, "Output exceeds 1048576 UTF-8 bytes.");
        return json;
    }

    public static void SaveFile(AuthoredSceneDocument document, string path, AssetRoot? assets = null)
    {
        string full = System.IO.Path.GetFullPath(path);
        string json = Write(document, full, assets); // Resolve resources at destination before any write.
        string temp = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, json, Utf8);
            File.Move(temp, full, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { throw Error("SCENE_WRITE", full, "$", null, e.Message, e); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static LoadedAuthoredScene Build(AuthoredSceneDocument doc, string file, AssetRoot? assets)
    {
        if (doc.Kind != Kind) throw Error("SCENE_KIND", file, "$.kind", null, $"Expected {Kind}; runtime saves are separate documents.");
        if (doc.Version is not (1 or 2)) throw Error("SCENE_VERSION", file, "$.version", null, "Only authored scene versions 1 and 2 are supported.");
        if (string.IsNullOrWhiteSpace(doc.Name)) throw Error("SCENE_NAME", file, "$.name", null, "Scene name cannot be empty.");
        if (doc.Resources is null || doc.Resources.Count > 128) throw Error("SCENE_LIMIT", file, "$.resources", null, "At most 128 resources are supported.");
        if (doc.Entities is null || doc.Entities.Count > 4096) throw Error("SCENE_LIMIT", file, "$.entities", null, "At most 4096 entities are supported.");
        var ids = new HashSet<Guid>();
        void Unique(Guid id, string path, Guid? entity = null)
        { if (id == Guid.Empty || !ids.Add(id)) throw Error("SCENE_ID", file, path, entity, "ID must be nonempty and unique across scene, persistent scope and entities."); }
        Unique(doc.Id, "$.id"); Unique(doc.PersistentScopeId, "$.persistentScopeId");
        var paths = new Dictionary<string, string>(StringComparer.Ordinal);
        assets ??= new AssetRoot(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(file))!);
        string sourceLogical;
        try { sourceLogical = assets.LogicalPathFor(file); }
        catch (AssetException e) { throw Error("SCENE_RESOURCE", file, "$", null, e.Message, e); }
        var logicalPaths = new Dictionary<string, TextureAsset>(StringComparer.Ordinal);
        for (int i = 0; i < doc.Resources.Count; i++)
        {
            var resource = doc.Resources[i]; string prefix = $"$.resources[{i}]";
            if (resource is null) throw Error("SCENE_RESOURCE", file, prefix, null, "Resource cannot be null.");
            if (string.IsNullOrWhiteSpace(resource.Key) || !paths.TryAdd(resource.Key, ""))
                throw Error("SCENE_RESOURCE", file, prefix + ".key", null, "Resource key must be nonempty and unique (case sensitive).");
            if (doc.Version == 1 && resource.Region is not null) throw Error("SCENE_VERSION", file, prefix + ".region", null, "Texture regions require authored version 2.");
            try
            {
                // Version 1 remains relative to its scene. The root supplies one namespace,
                // not a second meaning for an existing resource field.
                string logical = assets.Sibling(sourceLogical, resource.Path);
                var info = assets.ReadImageInfo(logical);
                paths[resource.Key] = info.Path;
                try { resource.Region?.ToRegion().Validate(info.Width, info.Height); }
                catch (ArgumentOutOfRangeException e) { throw Error("SCENE_RESOURCE", file, prefix + ".region", null, e.Message, e); }
                logicalPaths.Add(resource.Key, new TextureAsset(logical, resource.Region?.ToRegion()));
            }
            catch (AssetException e)
            { throw Error("SCENE_RESOURCE", file, prefix + ".path", null, e.Message, e); }
        }
        var entities = new Dictionary<Guid, int>();
        for (int i = 0; i < doc.Entities.Count; i++)
        {
            var entity = doc.Entities[i]; string p = $"$.entities[{i}]";
            if (entity is null) throw Error("SCENE_ENTITY", file, p, null, "Entity cannot be null.");
            Unique(entity.Id, p + ".id", entity.Id); entities.Add(entity.Id, i);
            if (entity.SceneId != doc.Id) throw Error("SCENE_REFERENCE", file, p + ".sceneId", entity.Id, "Authored entities must belong to this scene.");
            if (string.IsNullOrWhiteSpace(entity.Name)) throw Error("SCENE_NAME", file, p + ".name", entity.Id, "Name cannot be empty.");
            if (entity.Transform is null) throw Error("SCENE_TRANSFORM", file, p + ".transform", entity.Id, "Transform cannot be null.");
            try { entity.Transform.ToTransform().Validate(); }
            catch (ArgumentOutOfRangeException e) { throw Error("SCENE_TRANSFORM", file, p + ".transform", entity.Id, e.Message, e); }
            try { entity.Sprite?.ToSprite().Validate(); }
            catch (ArgumentOutOfRangeException e) { throw Error("SCENE_SPRITE", file, p + ".sprite", entity.Id, e.Message, e); }
            if (doc.Version == 1 && entity.Sprite is {} oldSprite && (oldSprite.FlipX || oldSprite.FlipY))
                throw Error("SCENE_VERSION", file, p + ".sprite", entity.Id, "Sprite flips require authored version 2.");
            if (entity.Sprite?.AssetKey is {} key && !paths.ContainsKey(key))
                throw Error("SCENE_RESOURCE", file, p + ".sprite.assetKey", entity.Id, $"Unregistered resource key '{key}'.");
        }
        foreach (bool ownership in new[] { false, true })
        {
            string field = ownership ? "ownerId" : "parentId";
            var depths = new Dictionary<Guid, int>();
            for (int i = 0; i < doc.Entities.Count; i++)
            {
                var path = new List<Guid>(); var visited = new HashSet<Guid>(); Guid? cursor = doc.Entities[i].Id;
                while (cursor is {} id && !depths.ContainsKey(id))
                {
                    int index = entities[id]; var entity = doc.Entities[index];
                    if (!visited.Add(id) || path.Count >= 512)
                        throw Error("SCENE_GRAPH", file, $"$.entities[{index}].{field}", id, "Cycle or relation depth exceeds 512.");
                    path.Add(id); Guid? target = ownership ? entity.OwnerId : entity.ParentId;
                    if (target is {} reference && !entities.ContainsKey(reference))
                        throw Error("SCENE_REFERENCE", file, $"$.entities[{index}].{field}", id, $"Missing entity {reference}.");
                    cursor = target;
                }
                int depth = cursor is {} known ? depths[known] : 0;
                for (int j = path.Count - 1; j >= 0; j--)
                {
                    if (++depth > 512) throw Error("SCENE_GRAPH", file, $"$.entities[{entities[path[j]]}].{field}", path[j], "Relation depth exceeds 512.");
                    depths[path[j]] = depth;
                }
            }
        }
        var world = new World(doc.PersistentScopeId); var scene = world.CreateScene(doc.Name, doc.Id);
        foreach (var entity in doc.Entities)
        { var created = world.Create(entity.Name, scene, entity.Transform.ToTransform(), entity.Id); created.Sprite = entity.Sprite?.ToSprite(); }
        foreach (var entity in doc.Entities)
        { var created = world.GetPersistent(entity.Id); created.ParentValue = entity.ParentId is {} parent ? world.GetPersistent(parent) : null; created.OwnerValue = entity.OwnerId is {} owner ? world.GetPersistent(owner) : null; }
        for (int i = 0; i < doc.Entities.Count; i++)
        {
            var entity = world.GetPersistent(doc.Entities[i].Id);
            try
            {
                var t = entity.WorldTransform;
                if (entity.Sprite is {} sprite)
                {
                    if (!float.IsFinite(sprite.Width * t.ScaleX) || !float.IsFinite(sprite.Height * t.ScaleY))
                        throw new ArgumentOutOfRangeException(nameof(entity), "World sprite extents exceed numeric range.");
                    t.GetBasis(out float a, out float b, out float c, out float d);
                    for (int corner = 0; corner < 4; corner++)
                    {
                        double x = (corner & 1) == 0 ? 0 : sprite.Width, y = (corner & 2) == 0 ? 0 : sprite.Height;
                        if (!float.IsFinite((float)(t.X + a*x + c*y)) || !float.IsFinite((float)(t.Y + b*x + d*y)))
                            throw new ArgumentOutOfRangeException(nameof(entity), "World sprite corners exceed numeric range.");
                    }
                }
            }
            catch (ArgumentOutOfRangeException e) { throw Error("SCENE_TRANSFORM", file, $"$.entities[{i}].transform", entity.PersistentId, e.Message, e); }
        }
        return new(doc, world, new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(paths), new AssetCatalog(assets, logicalPaths));
    }
    private static AuthoredSceneException Error(string code, string file, string path, Guid? id, string cause, Exception? inner = null)
        => new(code, file, path, id, cause, inner);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, AllowDuplicateProperties = false,
    RespectNullableAnnotations = true, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(AuthoredSceneDocument))]
internal partial class AuthoredSceneJsonContext : JsonSerializerContext;
