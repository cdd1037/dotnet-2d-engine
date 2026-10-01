using System.Text.Json.Nodes;

namespace GameAuthoringLab;

internal static class WorldPersistenceTests
{
    private static int _assertions;

    public static int Run()
    {
        _assertions = 0;
        RotationAndShear();
        PersistentIdentity();
        RoundTrip();
        InvalidSavesAreAtomic();
        NumericFailureIsAtomic();
        BoundedHierarchy();
        return _assertions;
    }

    private static void RotationAndShear()
    {
        var world = new World();
        Entity parent = world.Create("Rotated parent", transform: new Transform2D(10, 20, 2, 3, MathF.PI / 2));
        Entity child = world.CreateChild(parent, "Rotated child", new Transform2D(4, 5, 3, 2, MathF.PI / 4));
        Transform2D before = child.WorldTransform;
        Near(before.X, -5, "rotation changes child translation X");
        Near(before.Y, 28, "rotation changes child translation Y");
        Assert(MathF.Abs(before.Shear) > 0.1f, "nonuniform scaled rotation retains induced shear");
        Point actual = PointAt(before, 3, 7);
        Point nested = PointAt(parent.LocalTransform, PointAt(child.LocalTransform, 3, 7));
        Near(actual, nested, "full affine hierarchy point agrees with nested transforms");
        world.Reparent(child, null);
        NearMatrix(child.WorldTransform, before, "unparent preserves complete affine basis");
        Entity other = world.Create("Different parent", transform: new Transform2D(-30, 40, 1.5f, 0.7f, -0.9f, 0.3f));
        world.Reparent(child, other);
        NearMatrix(child.WorldTransform, before, "reparent preserves rotation and induced shear");
        world.SetOwner(child, null);
        world.Destroy(other);
        NearMatrix(child.WorldTransform, before, "destroy detaches affine survivor without changing shape");
        Throws<ArgumentOutOfRangeException>(() => child.LocalTransform = before with { Rotation = float.NaN }, "NaN rotation rejected");
        Throws<ArgumentOutOfRangeException>(() => child.LocalTransform = before with { Shear = float.PositiveInfinity }, "infinite shear rejected");

        var random = new Random(1977);
        for (int sample = 0; sample < 100; sample++)
        {
            var chain = new World();
            Entity node = chain.Create("Root", transform: RandomTransform());
            var transforms = new List<Transform2D> { node.LocalTransform };
            for (int i = 0; i < 4; i++)
            {
                node = chain.CreateChild(node, "Child", RandomTransform());
                transforms.Add(node.LocalTransform);
            }
            Point point = new(2.3, -1.7);
            for (int i = transforms.Count - 1; i >= 0; i--) point = PointAt(transforms[i], point);
            Near(PointAt(node.WorldTransform, 2.3, -1.7), point, "random affine chain preserves transformed point", 0.002);
            Transform2D original = node.WorldTransform;
            chain.Reparent(node, null);
            NearMatrix(node.WorldTransform, original, "random full affine detachment", 0.002);
        }
        Console.WriteLine("PASS radians, rotated/nonuniform parents, shear-preserving reparent/detach, 100 affine chains");
        Transform2D RandomTransform() => new((float)random.NextDouble() * 20 - 10, (float)random.NextDouble() * 20 - 10,
            0.5f + (float)random.NextDouble() * 1.5f, 0.5f + (float)random.NextDouble() * 1.5f,
            (float)random.NextDouble() * 6 - 3, (float)random.NextDouble() - 0.5f);
    }

    private static void PersistentIdentity()
    {
        var world = new World();
        Guid entityId = Guid.NewGuid(), sceneId = Guid.NewGuid();
        Scene scene = world.CreateScene("Stable room", sceneId);
        Entity entity = world.Create("Stable object", scene, persistentId: entityId);
        Assert(entity.PersistentId == entityId && scene.PersistentId == sceneId, "explicit persistent identities retained");
        Assert(ReferenceEquals(world.GetPersistent(entityId), entity) && ReferenceEquals(world.GetScene(sceneId), scene), "persistent lookup resolves objects");
        Throws<ArgumentException>(() => world.Create("Duplicate", persistentId: entityId), "duplicate entity persistent ID rejected");
        Throws<ArgumentException>(() => world.CreateScene("Duplicate", entityId), "cross-kind persistent ID duplicate rejected");
        Throws<ArgumentException>(() => world.Create("Empty", persistentId: Guid.Empty), "empty entity persistent ID rejected");
        Throws<ArgumentException>(() => new World(Guid.Empty), "empty persistent scene ID rejected");
        Throws<ArgumentException>(() => world.CreateScene("Empty", Guid.Empty), "empty room ID rejected");
        world.MakePersistent(entity);
        Assert(entity.PersistentId == entityId, "persistence and scene migration retain save identity");
        world.UnloadScene(scene);
        Throws<ArgumentException>(() => world.GetScene(sceneId), "unloaded stable scene lookup rejected");
        world.Destroy(entity);
        Assert(!world.TryGetPersistent(entityId, out _), "destroy removes persistent lookup");
        Console.WriteLine("PASS runtime IDs versus persistent GUIDs, cross-kind uniqueness and stale lookup");
    }

    private static void RoundTrip()
    {
        (World world, GameSaveState state) = Fixture();
        string json = ScenePersistence.Save(world, state);
        Assert(!json.Contains("runtimeId", StringComparison.OrdinalIgnoreCase) && !json.Contains("$type", StringComparison.Ordinal), "save contains no runtime handles or CLR types");
        var checks = new Dictionary<string, int>();
        LoadedScene loaded = ScenePersistence.Load(json, key =>
        {
            checks[key] = checks.GetValueOrDefault(key) + 1;
            return key is "item.bmp" or "player.bmp";
        });
        Assert(loaded.State == state, "explicit game state round-trips");
        Assert(checks.Count == 2 && checks.Values.All(count => count == 1), "each distinct external asset resolved once; built-in texture skipped");
        Assert(loaded.World.EntityCount == world.EntityCount, "all live entities reloaded");
        foreach (Entity source in world.Entities)
        {
            Entity target = loaded.World.GetPersistent(source.PersistentId);
            Assert(target.Id != source.Id && !ReferenceEquals(source, target), "reload gets distinct runtime object and ID");
            Assert(target.PersistentId == source.PersistentId && target.Name == source.Name, "reload retains stable identity and name");
            Assert(target.Scene.PersistentId == source.Scene.PersistentId, "reload retains scene membership");
            Assert(target.TransformParent?.PersistentId == source.TransformParent?.PersistentId
                && target.LifetimeOwner?.PersistentId == source.LifetimeOwner?.PersistentId, "separate parent and lifetime references round-trip");
            Assert(target.Sprite == source.Sprite && target.LocalTransform == source.LocalTransform, "sprite asset/layer/tint and affine local data round-trip exactly");
            NearMatrix(target.WorldTransform, source.WorldTransform, "world affine shape survives serialization");
            Assert(target.Behavior is null, "behavior code is not reconstructed from save data");
        }
        Assert(ScenePersistence.Save(loaded.World, loaded.State) == json, "save-load-save is deterministic with stable identities and order");
        LoadedScene second = ScenePersistence.Load(json, _ => true);
        Assert(second.World.GetPersistent(state.PlayerId!.Value).Id != loaded.World.GetPersistent(state.PlayerId.Value).Id,
            "loading the same save twice never reuses runtime IDs");
        Scene room = loaded.World.GetScene(state.ActiveSceneId!.Value);
        Entity player = loaded.World.GetPersistent(state.PlayerId!.Value);
        Entity held = loaded.World.GetPersistent(state.HeldItemId!.Value);
        Transform2D heldBefore = held.WorldTransform;
        loaded.World.UnloadScene(room);
        Assert(player.IsAlive && held.IsAlive && ReferenceEquals(player.Scene, loaded.World.PersistentScene), "loaded persistent player and held item survive room unload");
        NearMatrix(held.WorldTransform, heldBefore, "loaded held item's full shape survives room unload");
        Scene next = loaded.World.LoadedScenes.First(scene => !ReferenceEquals(scene, loaded.World.PersistentScene));
        loaded.World.Drop(held, next);
        loaded.World.UnloadScene(next);
        Assert(player.IsAlive && !held.IsAlive, "loaded dropped item unloads with destination room");
        var empty = new World();
        LoadedScene emptyLoaded = ScenePersistence.Load(ScenePersistence.Save(empty), _ => throw new InvalidOperationException("No assets expected"));
        Assert(emptyLoaded.World.EntityCount == 0 && emptyLoaded.State is null, "empty persistent-only world round-trips");
        Console.WriteLine("PASS source-generated JSON, stable GUIDs, distinct runtime IDs, independent references, sprites/state, lifecycle after reload");
    }

    private static void InvalidSavesAreAtomic()
    {
        (World world, GameSaveState state) = Fixture();
        string good = ScenePersistence.Save(world, state);
        LoadedScene current = new(world, state);
        RejectText("{", "malformed JSON");
        bool diagnosed = false;
        try { ScenePersistence.Load("{\n  \"version\": \"wrong\"}", _ => true); }
        catch (SceneFormatException error)
        {
            diagnosed = error.Message.Contains("$.version", StringComparison.Ordinal)
                && error.Message.Contains("line 2", StringComparison.Ordinal)
                && error.Message.Contains("byte column", StringComparison.Ordinal)
                && error.InnerException is System.Text.Json.JsonException;
        }
        Assert(diagnosed, "JSON errors report field, 1-based source location, and original cause");
        RejectText("null", "null document");
        RejectText(good[..^1] + ",\"version\":1}", "duplicate JSON property");
        Reject(root => root["version"] = 99, "unsupported format version");
        Reject(root => root["unknown"] = true, "unknown schema property");
        Reject(root => root.Remove("entities"), "missing required collection");
        Reject(root => root["entities"] = null, "null entity collection");
        Reject(root => root["entities"]!.AsArray().Add((JsonNode?)null), "null entity entry");
        Reject(root => root["scenes"]!.AsArray().Add((JsonNode?)null), "null scene entry");
        Reject(root => EntityNode(root, "Player")["id"] = Guid.Empty.ToString(), "empty stable ID");
        Reject(root => EntityNode(root, "Held item")["id"] = state.PlayerId!.Value.ToString(), "duplicate entity stable ID");
        Reject(root => root["scenes"]![1]!["id"] = state.PlayerId!.Value.ToString(), "cross-kind duplicate stable ID");
        Reject(root => EntityNode(root, "Player")["sceneId"] = Guid.NewGuid().ToString(), "missing scene reference");
        Reject(root => EntityNode(root, "Player")["parentId"] = Guid.NewGuid().ToString(), "missing transform parent");
        Reject(root => EntityNode(root, "Player")["ownerId"] = Guid.NewGuid().ToString(), "missing lifetime owner");
        Reject(root => EntityNode(root, "Player")["parentId"] = state.PlayerId!.Value.ToString(), "self parent cycle");
        Reject(root => EntityNode(root, "Player")["ownerId"] = state.PlayerId!.Value.ToString(), "self lifetime cycle");
        Reject(root => EntityNode(root, "Player")["parentId"] = state.HeldItemId!.Value.ToString(), "two-entity parent cycle");
        Reject(root => EntityNode(root, "Player")["ownerId"] = state.HeldItemId!.Value.ToString(), "two-entity lifetime cycle");
        Reject(root => EntityNode(root, "Player")["transform"] = null, "null transform");
        Reject(root => EntityNode(root, "Player")["transform"]!.AsObject().Remove("scaleX"), "missing numeric transform field");
        Reject(root => EntityNode(root, "Player")["transform"]!["scaleX"] = 0, "singular zero scale");
        Reject(root => EntityNode(root, "Player")["transform"]!["scaleY"] = -1, "negative scale");
        Reject(root => EntityNode(root, "Player")["transform"]!["rotation"] = "NaN", "nonfinite named number");
        Reject(root => EntityNode(root, "Player")["transform"]!["rotation"] = JsonNode.Parse("1e999"), "numeric float overflow");
        Reject(root => EntityNode(root, "Player")["sprite"]!["a"] = 2, "invalid sprite tint");
        Reject(root => EntityNode(root, "Player")["sprite"]!["assetKey"] = "  ", "empty asset key");
        Reject(root => EntityNode(root, "Player")["sprite"]!["assetKey"] = "missing.bmp", "missing external resource");
        Reject(root => root["state"]!["playerId"] = Guid.NewGuid().ToString(), "missing game state player");
        Reject(root => root["state"]!["heldItemId"] = Guid.NewGuid().ToString(), "missing game state held item");
        Reject(root => root["state"]!["activeSceneId"] = Guid.NewGuid().ToString(), "missing game state scene");
        Reject(root => root["state"]!.AsObject().Remove("roomIndex"), "missing explicit game state field");
        Reject(root => root["state"]!["roomIndex"] = -1, "negative game state index");
        Reject(root => root["state"]!["itemId"] = Guid.NewGuid().ToString(), "missing game state item");
        Reject(root => root["state"]!["itemRoomIndex"] = -2, "invalid game state item room");
        Reject(root => root["state"]!["playerId"] = null, "held item without saved player");
        Reject(root => root["scenes"]![0]!["persistent"] = false, "missing persistent scope");
        Reject(root => root["scenes"]![1]!["persistent"] = true, "multiple persistent scopes");
        Reject(root => root["scenes"]![0]!["name"] = "Wrong", "renamed persistent scope");
        Reject(root => EntityNode(root, "Player")["name"] = "", "empty entity name");
        Console.WriteLine("PASS malformed/schema/version/data/resource/reference/cycle failures preserve current world and state atomically");

        void Reject(Action<JsonObject> mutate, string message)
        {
            JsonObject root = JsonNode.Parse(good)!.AsObject();
            mutate(root);
            RejectText(root.ToJsonString(), message);
        }
        void RejectText(string json, string message)
        {
            Throws<SceneFormatException>(() => current = ScenePersistence.Load(json, key => key is "item.bmp" or "player.bmp"), message);
            Assert(ReferenceEquals(current.World, world) && current.State == state && world.GetPersistent(state.PlayerId!.Value).IsAlive,
                message + ": live world/state not replaced");
            Assert(ScenePersistence.Save(world, state) == good, message + ": live world contents unchanged");
        }
    }

    private static void NumericFailureIsAtomic()
    {
        (World world, GameSaveState state) = Fixture();
        JsonObject root = JsonNode.Parse(ScenePersistence.Save(world, state))!.AsObject();
        EntityNode(root, "Player")["transform"]!["scaleX"] = 1e30f;
        EntityNode(root, "Held item")["transform"]!["scaleX"] = 1e30f;
        Throws<SceneFormatException>(() => ScenePersistence.Load(root.ToJsonString(), _ => true), "world transform overflow rejected before returning candidate");
        JsonObject extentRoot = JsonNode.Parse(ScenePersistence.Save(world, state))!.AsObject();
        EntityNode(extentRoot, "Player")["sprite"]!["width"] = float.MaxValue;
        Throws<SceneFormatException>(() => ScenePersistence.Load(extentRoot.ToJsonString(), _ => true), "sprite extent overflow rejects load");
        JsonObject shearRoot = JsonNode.Parse(ScenePersistence.Save(world, state))!.AsObject();
        EntityNode(shearRoot, "Player")["transform"]!["shear"] = 1e38f;
        Throws<SceneFormatException>(() => ScenePersistence.Load(shearRoot.ToJsonString(), _ => true), "sheared sprite corner overflow rejects load");
        Entity player = world.GetPersistent(state.PlayerId!.Value);
        Entity held = world.GetPersistent(state.HeldItemId!.Value);
        Transform2D playerBefore = player.LocalTransform;
        held.LocalTransform = new Transform2D(0, 0, 1e30f, 1);
        player.LocalTransform = new Transform2D(0, 0, 1e30f, 1);
        Throws<ArgumentOutOfRangeException>(() => ScenePersistence.Save(world, state), "saving numerically unusable live hierarchy fails");
        player.LocalTransform = playerBefore;
        Console.WriteLine("PASS hierarchy numeric overflow rejects load/save without live-world mutation");
    }

    private static void BoundedHierarchy()
    {
        var world = new World();
        Entity root = world.Create("Chain root");
        Entity last = root;
        for (int i = 1; i < 512; i++) last = world.CreateChild(last, "Chain member");
        string allowed = ScenePersistence.Save(world);
        Assert(ScenePersistence.Load(allowed, _ => true).World.EntityCount == 512, "bounded deep hierarchy loads without recursion");
        world.CreateChild(last, "Beyond limit");
        Throws<SceneFormatException>(() => ScenePersistence.Save(world), "over-depth world rejected when saving");
        JsonObject json = JsonNode.Parse(allowed)!.AsObject();
        JsonObject extra = json["entities"]!.AsArray()[^1]!.DeepClone().AsObject();
        extra["id"] = Guid.NewGuid().ToString();
        extra["parentId"] = last.PersistentId.ToString();
        extra["ownerId"] = null;
        json["entities"]!.AsArray().Add((JsonNode)extra);
        Throws<SceneFormatException>(() => ScenePersistence.Load(json.ToJsonString(), _ => true), "over-depth serialized hierarchy rejected before construction");
        Console.WriteLine("PASS 512-level iterative graph checks and explicit depth-limit rejection");
    }

    private static (World, GameSaveState) Fixture()
    {
        var world = new World();
        Scene roomA = world.CreateScene("Room A"), roomB = world.CreateScene("Room B");
        Entity player = world.Create("Player", transform: new Transform2D(50, 60, 1.2f, 0.8f, 0.4f));
        player.Sprite = new Sprite2D(24, 32, AssetKey: "player.bmp", Layer: 10);
        Entity held = world.Create("Held item", roomA, new Transform2D(20, 30, 2, 0.8f, -0.7f));
        held.Sprite = new Sprite2D(10, 18, 0.8f, 0.7f, 0.6f, 0.9f, "item.bmp", 11);
        world.PickUp(held, player);
        Entity anchor = world.Create("Room anchor", roomA, new Transform2D(3, 7, 2, 3, 0.5f));
        Entity independent = world.Create("Independent relations", roomB, new Transform2D(5, 6, 0.7f, 1.2f, -0.3f, 0.5f));
        independent.Sprite = new Sprite2D(8, 12);
        world.Reparent(independent, anchor, keepWorldTransform: false);
        world.SetOwner(independent, player);
        Entity duplicateAsset = world.Create("Shared item asset", roomB);
        duplicateAsset.Sprite = new Sprite2D(12, 12, AssetKey: "item.bmp", Layer: -1);
        return (world, new GameSaveState { PlayerId = player.PersistentId, HeldItemId = held.PersistentId,
            ActiveSceneId = roomA.PersistentId, ItemId = held.PersistentId, ItemRoomIndex = -1, RoomIndex = 0, TransitionCount = 3, PickupCount = 5 });
    }

    private static JsonObject EntityNode(JsonObject root, string name)
        => root["entities"]!.AsArray().Select(node => node!.AsObject()).Single(node => node["name"]!.GetValue<string>() == name);

    private readonly record struct Point(double X, double Y);
    private static Point PointAt(Transform2D transform, double x, double y) => PointAt(transform, new Point(x, y));
    private static Point PointAt(Transform2D transform, Point point)
    {
        // Independent evaluation of translation * rotation * scale/shear.
        double x = transform.ScaleX * point.X + transform.Shear * point.Y;
        double y = transform.ScaleY * point.Y;
        return new(Math.Cos(transform.Rotation) * x - Math.Sin(transform.Rotation) * y + transform.X,
            Math.Sin(transform.Rotation) * x + Math.Cos(transform.Rotation) * y + transform.Y);
    }
    private static void NearMatrix(Transform2D actual, Transform2D expected, string message, double tolerance = 0.001)
    {
        Near(PointAt(actual, 0, 0), PointAt(expected, 0, 0), message + " origin", tolerance);
        Near(PointAt(actual, 1, 0), PointAt(expected, 1, 0), message + " X basis", tolerance);
        Near(PointAt(actual, 0, 1), PointAt(expected, 0, 1), message + " Y basis", tolerance);
    }
    private static void Near(Point actual, Point expected, string message, double tolerance = 0.001)
        => Assert(Math.Abs(actual.X - expected.X) < tolerance && Math.Abs(actual.Y - expected.Y) < tolerance, message);
    private static void Near(double actual, double expected, string message) => Assert(Math.Abs(actual - expected) < 0.001, message);
    private static void Throws<T>(Action action, string message) where T : Exception
    {
        bool threw = false;
        try { action(); }
        catch (T) { threw = true; }
        Assert(threw, message);
    }
    private static void Assert(bool value, string message)
    {
        _assertions++;
        if (!value) throw new InvalidOperationException("WORLD PERSISTENCE TEST FAIL: " + message);
    }
}
