using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameAuthoringLab;

// Deliberately explicit game data. No serialized CLR type names, code, delegates,
// runtime handles, generic property bag, or implicit reflection reconstruction.
internal sealed record GameSaveState
{
    [JsonRequired]
    public Guid? PlayerId { get; init; }
    [JsonRequired]
    public Guid? ActiveSceneId { get; init; }
    [JsonRequired]
    public Guid? HeldItemId { get; init; }
    [JsonRequired]
    public Guid? ItemId { get; init; }
    [JsonRequired]
    public int ItemRoomIndex { get; init; }
    [JsonRequired]
    public int RoomIndex { get; init; }
    [JsonRequired]
    public int TransitionCount { get; init; }
    [JsonRequired]
    public int PickupCount { get; init; }
}

internal sealed record LoadedScene(World World, GameSaveState? State);

internal sealed class SceneFormatException(string message, Exception? inner = null) : Exception(message, inner);

internal static class ScenePersistence
{
    public const int FormatVersion = 1;
    private const int MaximumCharacters = 16 * 1024 * 1024;
    private const int MaximumEntities = 100_000;
    private const int MaximumScenes = 10_000;
    private const int MaximumHierarchyDepth = 512;

    public static string Save(World world, GameSaveState? state = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        var scenes = new List<SceneRecord>();
        foreach (Scene scene in world.LoadedScenes)
            scenes.Add(new SceneRecord { Id = scene.PersistentId, Name = scene.Name,
                Persistent = ReferenceEquals(scene, world.PersistentScene) });
        var entities = new List<EntityRecord>();
        foreach (Entity entity in world.Entities)
        {
            if (!entity.IsAlive) continue;
            // Do not emit saves whose transforms cannot be evaluated on reload.
            ValidateWorldData(entity);
            entities.Add(new EntityRecord
            {
                Id = entity.PersistentId, Name = entity.Name, SceneId = entity.Scene.PersistentId,
                ParentId = entity.TransformParent?.PersistentId, OwnerId = entity.LifetimeOwner?.PersistentId,
                Transform = TransformRecord.From(entity.LocalTransform),
                Sprite = entity.Sprite is { } sprite ? SpriteRecord.From(sprite) : null
            });
        }
        var document = new WorldSaveDocument { Version = entities.Any(e => e.Sprite is { FlipX: true } or { FlipY: true }) ? 2 : FormatVersion, Scenes = scenes, Entities = entities, State = state };
        // Validate the same explicit schema and references before saving it.
        ValidateDocument(document, assetExists: null);
        string json = JsonSerializer.Serialize(document, SceneJsonContext.Default.WorldSaveDocument);
        if (json.Length > MaximumCharacters) throw new SceneFormatException("Save exceeds the 16 Mi-character limit.");
        return json;
    }

    // No existing world is ever changed. Callers swap the returned world/state
    // only after any game-specific reference/behavior binding also succeeds.
    public static LoadedScene Load(string json, Func<string, bool> assetExists)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(assetExists);
        if (json.Length > MaximumCharacters) throw new SceneFormatException("Save exceeds the 16 Mi-character limit.");
        WorldSaveDocument document;
        try
        {
            document = JsonSerializer.Deserialize(json, SceneJsonContext.Default.WorldSaveDocument)
                ?? throw new SceneFormatException("Save document cannot be null.");
        }
        catch (JsonException error)
        {
            string location = error.Path ?? "$";
            string line = error.LineNumber is { } lineNumber ? (lineNumber + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) : "unknown";
            string column = error.BytePositionInLine is { } bytePosition ? (bytePosition + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) : "unknown";
            throw new SceneFormatException($"Malformed save JSON at {location}, line {line}, byte column {column}: {error.Message}", error);
        }
        ValidateDocument(document, assetExists);
        SceneRecord persistent = document.Scenes.Single(scene => scene.Persistent);
        var world = new World(persistent.Id);
        foreach (SceneRecord scene in document.Scenes)
            if (!scene.Persistent) world.CreateScene(scene.Name, scene.Id);
        foreach (EntityRecord entity in document.Entities)
        {
            Entity restored = world.Create(entity.Name, world.GetScene(entity.SceneId), entity.Transform.ToTransform(), entity.Id);
            restored.Sprite = entity.Sprite?.ToSprite();
        }
        // Both graphs were checked in linear time before constructing objects.
        // Binding directly avoids quadratic cycle scans for deep valid saves.
        foreach (EntityRecord entity in document.Entities)
        {
            Entity restored = world.GetPersistent(entity.Id);
            restored.ParentValue = entity.ParentId is { } parent ? world.GetPersistent(parent) : null;
            restored.OwnerValue = entity.OwnerId is { } owner ? world.GetPersistent(owner) : null;
        }
        try
        {
            foreach (Entity entity in world.Entities) ValidateWorldData(entity);
        }
        catch (ArgumentOutOfRangeException error)
        {
            throw new SceneFormatException("Saved hierarchy or sprite geometry exceeds supported numeric range: " + error.Message, error);
        }
        return new LoadedScene(world, document.State);
    }

    private static void ValidateDocument(WorldSaveDocument document, Func<string, bool>? assetExists)
    {
        if (document.Version is not (1 or 2)) throw new SceneFormatException($"Unsupported save version {document.Version}.");
        if (document.Scenes is null || document.Entities is null || document.Scenes.Count is < 1 or > MaximumScenes
            || document.Entities.Count > MaximumEntities) throw new SceneFormatException("Invalid scene/entity collection or count.");
        var allIds = new HashSet<Guid>();
        var sceneIds = new HashSet<Guid>();
        int persistentCount = 0;
        foreach (SceneRecord scene in document.Scenes)
        {
            if (scene is null) throw new SceneFormatException("Scene entry cannot be null.");
            RequireId(scene.Id, allIds);
            RequireName(scene.Name, "scene");
            sceneIds.Add(scene.Id);
            if (scene.Persistent)
            {
                persistentCount++;
                if (scene.Name != "Persistent") throw new SceneFormatException("Persistent scope must be named Persistent.");
            }
        }
        if (persistentCount != 1) throw new SceneFormatException("Exactly one persistent scene is required.");
        var entities = new Dictionary<Guid, EntityRecord>();
        var checkedAssets = new HashSet<string>(StringComparer.Ordinal);
        foreach (EntityRecord entity in document.Entities)
        {
            if (entity is null) throw new SceneFormatException("Entity entry cannot be null.");
            RequireId(entity.Id, allIds);
            RequireName(entity.Name, "entity");
            if (!sceneIds.Contains(entity.SceneId)) throw new SceneFormatException($"Missing scene reference for entity {entity.Id}.");
            if (entity.Transform is null) throw new SceneFormatException("Entity transform cannot be null.");
            try
            {
                entity.Transform.ToTransform().Validate();
                entity.Sprite?.ToSprite().Validate();
            }
            catch (ArgumentOutOfRangeException error) { throw new SceneFormatException($"Invalid transform/sprite data for entity '{entity.Name}' ({entity.Id}): {error.Message}", error); }
            if (document.Version == 1 && entity.Sprite is { } oldSprite && (oldSprite.FlipX || oldSprite.FlipY))
                throw new SceneFormatException("Sprite flips require save version 2.");
            if (entity.Sprite?.AssetKey is { } key && checkedAssets.Add(key) && assetExists is not null && !assetExists(key))
                throw new SceneFormatException($"Missing sprite asset '{key}'.");
            entities.Add(entity.Id, entity);
        }
        foreach (EntityRecord entity in document.Entities)
        {
            RequireReference(entity.ParentId, entities, "transform parent");
            RequireReference(entity.OwnerId, entities, "lifetime owner");
        }
        ValidateGraph(entities, ownership: false);
        ValidateGraph(entities, ownership: true);
        if (document.State is { } state)
        {
            RequireReference(state.PlayerId, entities, "state player");
            RequireReference(state.HeldItemId, entities, "state held item");
            RequireReference(state.ItemId, entities, "state item");
            if (state.ItemRoomIndex < -1) throw new SceneFormatException("Item room index must be -1 (held) or nonnegative.");
            if (state.ActiveSceneId is { } scene && !sceneIds.Contains(scene)) throw new SceneFormatException("Missing state active scene.");
            if (state.RoomIndex < 0 || state.TransitionCount < 0 || state.PickupCount < 0)
                throw new SceneFormatException("Game counters and room index cannot be negative.");
            if (state.HeldItemId is { } held && (state.PlayerId is not { } player
                || entities[held].ParentId != player || entities[held].OwnerId != player))
                throw new SceneFormatException("Held item must have the saved player as both parent and owner.");
        }
    }

    private static void ValidateWorldData(Entity entity)
    {
        Transform2D transform = entity.WorldTransform;
        if (entity.Sprite is not { } sprite) return;
        transform.GetBasis(out float m11, out float m12, out float m21, out float m22);
        if (!float.IsFinite(sprite.Width * transform.ScaleX) || !float.IsFinite(sprite.Height * transform.ScaleY))
            throw new ArgumentOutOfRangeException(nameof(entity), "Sprite scaled extents exceed numeric range.");
        for (int corner = 0; corner < 4; corner++)
        {
            double x = (corner & 1) == 0 ? 0 : sprite.Width;
            double y = (corner & 2) == 0 ? 0 : sprite.Height;
            if (!float.IsFinite((float)(transform.X + m11 * x + m21 * y))
                || !float.IsFinite((float)(transform.Y + m12 * x + m22 * y)))
                throw new ArgumentOutOfRangeException(nameof(entity), "Sprite affine corners exceed numeric range.");
        }
    }

    private static void RequireId(Guid id, HashSet<Guid> ids)
    {
        if (id == Guid.Empty || !ids.Add(id)) throw new SceneFormatException("Persistent IDs must be nonempty and unique across scenes and entities.");
    }

    private static void RequireName(string? value, string kind)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new SceneFormatException($"A {kind} name cannot be empty.");
    }

    private static void RequireReference(Guid? id, Dictionary<Guid, EntityRecord> entities, string kind)
    {
        if (id is { } value && !entities.ContainsKey(value)) throw new SceneFormatException($"Missing {kind} reference {value}.");
    }

    private static void ValidateGraph(Dictionary<Guid, EntityRecord> entities, bool ownership)
    {
        var depths = new Dictionary<Guid, int>();
        var path = new List<Guid>();
        var inPath = new HashSet<Guid>();
        foreach (Guid start in entities.Keys)
        {
            if (depths.ContainsKey(start)) continue;
            path.Clear();
            inPath.Clear();
            Guid? cursor = start;
            while (cursor is { } id && !depths.ContainsKey(id))
            {
                if (!inPath.Add(id))
                    throw new SceneFormatException(ownership ? "Lifetime ownership cycle in save." : "Transform parent cycle in save.");
                path.Add(id);
                if (path.Count > MaximumHierarchyDepth)
                    throw new SceneFormatException("Saved relation hierarchy exceeds the 512-entity depth limit.");
                cursor = ownership ? entities[id].OwnerId : entities[id].ParentId;
            }
            int depth = cursor is { } known ? depths[known] : 0;
            for (int i = path.Count - 1; i >= 0; i--)
            {
                if (++depth > MaximumHierarchyDepth)
                    throw new SceneFormatException("Saved relation hierarchy exceeds the 512-entity depth limit.");
                depths[path[i]] = depth;
            }
        }
    }

}

internal sealed class WorldSaveDocument
{
    public required int Version { get; init; }
    public required List<SceneRecord> Scenes { get; init; }
    public required List<EntityRecord> Entities { get; init; }
    public required GameSaveState? State { get; init; }
}

internal sealed class SceneRecord
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required bool Persistent { get; init; }
}

internal sealed class EntityRecord
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required Guid SceneId { get; init; }
    public required Guid? ParentId { get; init; }
    public required Guid? OwnerId { get; init; }
    public required TransformRecord Transform { get; init; }
    public required SpriteRecord? Sprite { get; init; }
}

internal sealed class TransformRecord
{
    public required float X { get; init; }
    public required float Y { get; init; }
    public required float ScaleX { get; init; }
    public required float ScaleY { get; init; }
    public required float Rotation { get; init; }
    public required float Shear { get; init; }
    internal Transform2D ToTransform() => new(X, Y, ScaleX, ScaleY, Rotation, Shear);
    internal static TransformRecord From(Transform2D value) => new()
    { X = value.X, Y = value.Y, ScaleX = value.ScaleX, ScaleY = value.ScaleY, Rotation = value.Rotation, Shear = value.Shear };
}

internal sealed class SpriteRecord
{
    public required float Width { get; init; }
    public required float Height { get; init; }
    public required float R { get; init; }
    public required float G { get; init; }
    public required float B { get; init; }
    public required float A { get; init; }
    public required string? AssetKey { get; init; }
    public required int Layer { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool FlipX { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool FlipY { get; init; }
    internal Sprite2D ToSprite() => new(Width, Height, R, G, B, A, AssetKey, Layer, FlipX, FlipY);
    internal static SpriteRecord From(Sprite2D value) => new()
    { Width = value.Width, Height = value.Height, R = value.R, G = value.G, B = value.B, A = value.A, AssetKey = value.AssetKey, Layer = value.Layer, FlipX = value.FlipX, FlipY = value.FlipY };
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, AllowDuplicateProperties = false,
    RespectNullableAnnotations = true, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(WorldSaveDocument))]
internal partial class SceneJsonContext : JsonSerializerContext;
