namespace GameAuthoringLab;

internal static class WorldSelfTests
{
    private static int _assertions;

    public static int Run()
    {
        _assertions = 0;
        IdentityAndValidation();
        IndependentRelations();
        PickupDropAndRoomUnload();
        MixedGraphSurvivors();
        NumericFailureAtomicity();
        BehaviorMutation();
        ExtractionAndAllocations();
        return _assertions;
    }

    private static void IdentityAndValidation()
    {
        var world = new World();
        var otherWorld = new World();
        Scene room = world.CreateScene("Room");
        Entity entity = world.Create("Object", room);
        Entity foreign = otherWorld.Create("Foreign");
        EntityId id = entity.Id;
        Assert(id.Value > 0 && id != foreign.Id, "IDs are nonzero and distinct across worlds");
        Assert(ReferenceEquals(world.Get(id), entity), "ID resolves to stable object");
        Assert(entity.LocalTransform == Transform2D.Identity && entity.TransformParent is null
            && entity.LifetimeOwner is null && ReferenceEquals(entity.Scene, room), "root defaults are explicit");
        Assert(ReferenceEquals(world.Create("Global").Scene, world.PersistentScene), "unspecified scene uses persistent scope");
        Throws<ArgumentException>(() => world.Get(default), "zero ID rejected");
        Throws<ArgumentException>(() => world.Get(foreign.Id), "foreign ID rejected");
        Throws<ArgumentException>(() => world.Destroy(foreign), "foreign object rejected");
        Throws<ArgumentException>(() => world.Create("Wrong scene", otherWorld.PersistentScene), "foreign scene rejected");
        Throws<ArgumentOutOfRangeException>(() => entity.LocalTransform = default, "zero scale rejected");
        Throws<ArgumentOutOfRangeException>(() => entity.LocalTransform = new Transform2D(0, 0, -1, 1), "negative scale rejected");
        Throws<ArgumentOutOfRangeException>(() => entity.LocalTransform = new Transform2D(float.NaN, 0), "NaN position rejected");
        Throws<ArgumentOutOfRangeException>(() => entity.Sprite = new Sprite2D(1, 1, A: 2), "invalid alpha rejected");
        Throws<ArgumentOutOfRangeException>(() => entity.Sprite = new Sprite2D(-1, 1), "negative sprite size rejected");
        Throws<ArgumentOutOfRangeException>(() => entity.Sprite = new Sprite2D(float.PositiveInfinity, 1), "infinite sprite size rejected");
        Throws<ArgumentOutOfRangeException>(() => world.Update(float.NaN), "NaN dt rejected");
        Throws<ArgumentOutOfRangeException>(() => world.Update(-1), "negative dt rejected");
        world.Destroy(entity);
        Assert(!entity.IsAlive && !world.TryGet(id, out _), "destroy invalidates ID lookup");
        Throws<ArgumentException>(() => world.Get(id), "stale ID rejected");
        Throws<InvalidOperationException>(() => world.Destroy(entity), "double destroy rejected");
        Throws<InvalidOperationException>(() => entity.LocalTransform = Transform2D.Identity, "stale property mutation rejected");
        Throws<InvalidOperationException>(() => world.Reparent(world.Create("Live"), entity), "dead parent rejected");
        Assert(world.Create("Replacement").Id != id, "destroyed IDs never reused");
        world.UnloadScene(room);
        Throws<ArgumentException>(() => world.Create("Stale room", room), "unloaded scene rejected");
        Throws<ArgumentException>(() => world.UnloadScene(room), "double unload rejected");
        Throws<InvalidOperationException>(() => world.UnloadScene(world.PersistentScene), "persistent scope cannot unload");
        Console.WriteLine("PASS world IDs, stale/foreign handles, component validation, scene guards");
    }

    private static void IndependentRelations()
    {
        var world = new World();
        Scene room = world.CreateScene("Room");
        Scene another = world.CreateScene("Another room");
        Entity parent = world.Create("Transform parent", room, new Transform2D(100, 50, 2, 3));
        Entity child = world.CreateChild(parent, "Default child", new Transform2D(10, 20, 4, 5));
        Assert(ReferenceEquals(child.TransformParent, parent) && ReferenceEquals(child.LifetimeOwner, parent)
            && ReferenceEquals(child.Scene, room), "CreateChild sets three convenient defaults");
        Near(child.WorldTransform, new Transform2D(120, 110, 8, 15), "parent translation and scale compose");
        world.SetOwner(child, null);
        world.MoveToScene(child, another);
        Assert(ReferenceEquals(child.TransformParent, parent) && child.LifetimeOwner is null
            && ReferenceEquals(child.Scene, another), "scene and owner changes leave transform parent alone");
        Transform2D before = child.WorldTransform;
        world.Reparent(child, null);
        Near(child.WorldTransform, before, "unparent defaults to preserving world transform");
        Assert(child.LifetimeOwner is null && ReferenceEquals(child.Scene, another), "reparent leaves other relationships alone");
        world.Reparent(child, parent);
        Near(child.WorldTransform, before, "reparent preserves translation and nonuniform scale");
        Throws<InvalidOperationException>(() => world.Reparent(parent, child), "transform cycle rejected");
        Throws<InvalidOperationException>(() => world.Reparent(child, child), "self parent rejected");
        Assert(parent.TransformParent is null, "rejected reparent is atomic");
        world.SetOwner(child, parent);
        Throws<InvalidOperationException>(() => world.SetOwner(parent, child), "ownership cycle rejected");
        Throws<InvalidOperationException>(() => world.SetOwner(child, child), "self owner rejected");
        world.SetOwner(child, null);
        world.Destroy(parent);
        Assert(child.IsAlive && child.TransformParent is null, "transform-only child survives parent destruction");
        Near(child.WorldTransform, before, "surviving child keeps world transform on destruction");
        Entity owner = world.Create("Owner", room);
        world.SetOwner(child, owner);
        Entity grandchild = world.CreateChild(child, "Owned grandchild");
        world.MoveToScene(grandchild, world.PersistentScene);
        world.Destroy(owner);
        Assert(!child.IsAlive && !grandchild.IsAlive, "explicit destruction follows ownership across scenes, including persistent");

        Entity localParent = world.Create("Local parent", room, new Transform2D(20, 30, 2, 2));
        Entity localChild = world.Create("Local child", room, new Transform2D(3, 4));
        world.Reparent(localChild, localParent, keepWorldTransform: false);
        Near(localChild.WorldTransform, new Transform2D(26, 38, 2, 2), "explicit local-space reparent");
        Console.WriteLine("PASS independent transform/owner/scene relations, world-stable detachment, cycle rejection");
    }

    private static void PickupDropAndRoomUnload()
    {
        var world = new World();
        Scene room = world.CreateScene("Room A");
        Scene nextRoom = world.CreateScene("Room B");
        Entity roomRoot = world.Create("Room root", room, new Transform2D(20, 40, 2, 3));
        Entity player = world.CreateChild(roomRoot, "Player", new Transform2D(10, 20, 2, 2));
        Entity hat = world.CreateChild(player, "Hat", new Transform2D(0, -5));
        Entity item = world.Create("Item", room, new Transform2D(130, 100, 3, 4));
        Entity contents = world.CreateChild(item, "Item contents");
        Transform2D playerBefore = player.WorldTransform, hatBefore = hat.WorldTransform, itemBefore = item.WorldTransform;
        EntityId playerId = player.Id, itemId = item.Id;
        world.MakePersistent(player);
        Assert(player.LifetimeOwner is null && player.TransformParent is null, "persistent root detaches external relationships");
        Assert(ReferenceEquals(player.Scene, world.PersistentScene) && ReferenceEquals(hat.Scene, world.PersistentScene), "persistence includes owned children");
        Near(player.WorldTransform, playerBefore, "persistence retains player world transform");
        Near(hat.WorldTransform, hatBefore, "persistence retains child world transform");
        world.PickUp(item, player);
        Near(item.WorldTransform, itemBefore, "pickup retains world transform");
        Assert(item.Id == itemId && ReferenceEquals(item.TransformParent, player) && ReferenceEquals(item.LifetimeOwner, player), "pickup preserves identity and sets both relationships");
        Assert(ReferenceEquals(item.Scene, world.PersistentScene) && ReferenceEquals(contents.Scene, world.PersistentScene), "pickup moves owned subtree to carrier scope");
        player.LocalTransform = player.LocalTransform with { X = player.LocalTransform.X + 10 };
        Near(item.WorldTransform, itemBefore with { X = itemBefore.X + 10 }, "carried item follows player");
        Transform2D droppedAt = item.WorldTransform;
        world.Drop(item, room, roomRoot);
        Near(item.WorldTransform, droppedAt, "drop with scaled parent preserves world transform");
        Assert(item.Id == itemId && item.LifetimeOwner is null && ReferenceEquals(item.TransformParent, roomRoot)
            && ReferenceEquals(item.Scene, room) && ReferenceEquals(contents.Scene, room), "drop keeps identity and restores room scope");
        world.PickUp(item, player);
        Entity crossSceneOwned = world.Create("Nonpersistent owned elsewhere", nextRoom);
        world.SetOwner(crossSceneOwned, roomRoot);
        Entity protectedEntity = world.Create("Explicit persistent entity", world.PersistentScene, new Transform2D(1, 2));
        world.Reparent(protectedEntity, roomRoot, keepWorldTransform: false);
        world.SetOwner(protectedEntity, roomRoot);
        Transform2D protectedBefore = protectedEntity.WorldTransform;
        world.UnloadScene(room);
        Assert(!room.IsLoaded && !roomRoot.IsAlive && !crossSceneOwned.IsAlive, "room unload follows nonpersistent owned descendants");
        Assert(player.IsAlive && hat.IsAlive && item.IsAlive && contents.IsAlive, "persistent player and held equipment survive room unload");
        Assert(player.Id == playerId && item.Id == itemId && ReferenceEquals(world.Get(itemId), item), "scene transition keeps the same IDs and objects");
        Assert(protectedEntity.IsAlive && protectedEntity.TransformParent is null && protectedEntity.LifetimeOwner is null, "persistent membership is an unload boundary");
        Near(protectedEntity.WorldTransform, protectedBefore, "unload detaches persistent survivor world-stably");
        Near(item.WorldTransform, droppedAt, "held item unchanged by room unload");
        world.Drop(item, nextRoom);
        world.UnloadScene(nextRoom);
        Assert(player.IsAlive && !item.IsAlive && !contents.IsAlive, "dropped item unloads with new room while player survives");

        Entity carrier = world.Create("Carrier");
        Entity candidate = world.Create("Candidate", transform: new Transform2D(5, 7));
        world.SetOwner(carrier, candidate);
        Throws<InvalidOperationException>(() => world.PickUp(candidate, carrier), "pickup ownership cycle rejected before reparent");
        Assert(candidate.TransformParent is null && candidate.LifetimeOwner is null, "failed pickup does not partially reparent");
        Near(candidate.WorldTransform, new Transform2D(5, 7), "failed pickup preserves transform");
        var foreign = new World();
        Throws<ArgumentException>(() => world.Drop(candidate, foreign.PersistentScene), "drop rejects foreign scene before mutation");
        Throws<ArgumentException>(() => world.PickUp(candidate, foreign.Create("Foreign carrier")), "pickup rejects foreign carrier");
        Console.WriteLine("PASS pickup/drop identity and world transforms, persistent player, room unload, atomic rejection");
    }

    private static void BehaviorMutation()
    {
        var world = new World();
        int sourceTicks = 0, doomedTicks = 0, newbornTicks = 0;
        Entity source = world.Create("Source");
        Entity doomed = world.Create("Doomed");
        doomed.Behavior = new Callback((_, _) => doomedTicks++);
        source.Behavior = new Callback((self, _) =>
        {
            sourceTicks++;
            world.Destroy(doomed);
            world.Create("Newborn").Behavior = new Callback((_, _) => newbornTicks++);
            world.Destroy(self);
        });
        world.Update(1f / 60);
        Assert(sourceTicks == 1 && doomedTicks == 0 && newbornTicks == 0 && world.EntityCount == 1, "destroyed entries skipped and newborn waits until next tick");
        world.Update(1f / 60);
        Assert(newbornTicks == 1, "newborn updates on next tick after compaction");
        Entity exceptional = world.Create("Exceptional");
        exceptional.Behavior = new Callback((self, _) =>
        {
            world.Destroy(self);
            throw new TestBehaviorException();
        });
        Throws<TestBehaviorException>(() => world.Update(0), "behavior errors propagate");
        world.Update(0);
        Assert(!exceptional.IsAlive && world.EntityCount == 1 && newbornTicks == 3, "behavior exception resets update guard and compacts destroyed entries");
        Entity recursive = world.Create("Recursive");
        recursive.Behavior = new Callback((_, _) => world.Update(0));
        Throws<InvalidOperationException>(() => world.Update(0), "recursive update rejected");
        recursive.Behavior = null;
        world.Update(0);
        Console.WriteLine("PASS behavior updates, spawn/destroy during update, exception recovery, reentrancy guard");
    }

    private static void NumericFailureAtomicity()
    {
        var world = new World();
        Scene room = world.CreateScene("Room");
        Entity wideParent = world.Create("Wide parent", room, new Transform2D(-3e38f, -3e38f, 2, 2));
        Entity wideChild = world.Create("Wide child", room, new Transform2D(3e38f, 3e38f));
        world.Reparent(wideChild, wideParent);
        Assert(wideChild.LocalTransform.X == 3e38f && wideChild.LocalTransform.Y == 3e38f,
            "wide relative intermediates retain representable final coordinates");
        Assert(wideChild.WorldTransform.X == 3e38f && wideChild.WorldTransform.Y == 3e38f,
            "wide compose intermediates retain representable final coordinates");
        Entity tinyRoot = world.Create("Tiny root", room, new Transform2D(0, 0, 1e-30f, 1));
        Entity hugeMiddle = world.CreateChild(tinyRoot, "Huge middle", new Transform2D(0, 0, 1e30f, 1));
        Entity hugeLeaf = world.CreateChild(hugeMiddle, "Huge leaf", new Transform2D(0, 0, 1e30f, 1));
        Assert(float.IsFinite(hugeLeaf.WorldTransform.ScaleX) && hugeLeaf.WorldTransform.ScaleX > 1e29f,
            "wide accumulation handles compensating ancestors before narrowing");
        Entity root = world.Create("Root", room, new Transform2D(0, 0, 1e30f, 1));
        Entity overflowing = world.CreateChild(root, "Overflow", new Transform2D(0, 0, 1e30f, 1));
        Throws<ArgumentOutOfRangeException>(() => _ = overflowing.WorldTransform, "composed scale overflow rejected");
        // Keep-local detachment is an explicit recovery route for bad authored data.
        world.Reparent(overflowing, null, keepWorldTransform: false);
        Near(overflowing.WorldTransform, overflowing.LocalTransform, "local-space detach recovers invalid hierarchy");

        Entity opposite = world.Create("Far away", room, new Transform2D(-float.MaxValue, 0));
        Entity far = world.Create("Far object", room, new Transform2D(float.MaxValue, 0));
        Throws<ArgumentOutOfRangeException>(() => world.Reparent(far, opposite), "unrepresentable relative position rejected");
        Assert(far.TransformParent is null && far.LocalTransform.X == float.MaxValue, "failed keep-world reparent is atomic");
        Throws<ArgumentOutOfRangeException>(() => world.PickUp(far, opposite), "unrepresentable pickup rejected");
        Assert(far.TransformParent is null && far.LifetimeOwner is null && ReferenceEquals(far.Scene, room), "numeric pickup failure preserves all relations");

        Entity survivor = world.Create("Survivor", world.PersistentScene, new Transform2D(0, 0, 1e30f, 1));
        world.Reparent(survivor, root, keepWorldTransform: false);
        Throws<ArgumentOutOfRangeException>(() => world.UnloadScene(room), "unload rejects unrepresentable survivor transform");
        Assert(room.IsLoaded && root.IsAlive && overflowing.IsAlive && survivor.IsAlive
            && ReferenceEquals(survivor.TransformParent, root), "failed unload has no partial deletion or detachment");
        survivor.LocalTransform = Transform2D.Identity;
        world.UnloadScene(room);
        Assert(!root.IsAlive && survivor.IsAlive && survivor.TransformParent is null, "repairing local data permits unload retry");
        Console.WriteLine("PASS transform overflow detection, atomic failures, local-space repair and unload retry");
    }

    private static void MixedGraphSurvivors()
    {
        var world = new World();
        Scene room = world.CreateScene("Room");
        Entity outside = world.Create("External transform parent", room, new Transform2D(10, 20, 2, 3));
        Entity player = world.Create("Player", room, new Transform2D(30, 40));
        Entity equipment = world.Create("Owned equipment", room, new Transform2D(5, 6));
        world.SetOwner(equipment, player);
        world.Reparent(equipment, outside, keepWorldTransform: false);
        Entity neighbor = world.Create("Transform-only neighbor", room);
        world.Reparent(neighbor, player, keepWorldTransform: false);
        Transform2D equipmentBefore = equipment.WorldTransform;
        world.MakePersistent(player);
        Assert(ReferenceEquals(equipment.Scene, world.PersistentScene) && equipment.TransformParent is null
            && ReferenceEquals(equipment.LifetimeOwner, player), "persistence retains owned child with external transform parent");
        Near(equipment.WorldTransform, equipmentBefore, "external-parented owned child remains world-stable");
        Assert(ReferenceEquals(neighbor.Scene, room), "persistence does not retain transform-only neighbors");
        world.UnloadScene(room);
        Assert(player.IsAlive && equipment.IsAlive && !outside.IsAlive && !neighbor.IsAlive, "mixed graph unload follows explicit scopes");

        Scene chainRoom = world.CreateScene("Alternating chain");
        Entity doomedA = world.Create("Doomed A", chainRoom, new Transform2D(10, 20, 2, 3));
        Entity savedA = world.Create("Saved A", transform: new Transform2D(5, 6, 2, 2));
        Entity doomedB = world.Create("Doomed B", chainRoom, new Transform2D(7, 8, 3, 4));
        Entity savedB = world.Create("Saved B", transform: new Transform2D(9, 10));
        world.Reparent(savedA, doomedA, keepWorldTransform: false);
        world.Reparent(doomedB, savedA, keepWorldTransform: false);
        world.Reparent(savedB, doomedB, keepWorldTransform: false);
        Transform2D savedABefore = savedA.WorldTransform, savedBBefore = savedB.WorldTransform;
        world.UnloadScene(chainRoom);
        Assert(savedA.IsAlive && savedB.IsAlive && !doomedA.IsAlive && !doomedB.IsAlive
            && savedA.TransformParent is null && savedB.TransformParent is null, "alternating transform chain survivors all detach");
        Near(savedA.WorldTransform, savedABefore, "first alternating survivor remains world-stable");
        Near(savedB.WorldTransform, savedBBefore, "second alternating survivor precomputed before any deletion");
        Console.WriteLine("PASS mixed owner/transform graphs and alternating scene-survivor chains");
    }

    private static void ExtractionAndAllocations()
    {
        var world = new World();
        Entity parent = world.Create("Parent", transform: new Transform2D(10, 20, 2, 3));
        Entity first = world.CreateChild(parent, "First", new Transform2D(5, 7, 4, 5));
        first.Sprite = new Sprite2D(2, 3, 0.1f, 0.2f, 0.3f, 0.4f);
        Entity second = world.Create("Second");
        second.Sprite = new Sprite2D(8, 9);
        var batch = new SpriteBatch();
        world.ExtractSprites(batch);
        Assert(batch.Count == 2 && batch.Capacity >= 2, "extract skips objects without sprites and grows storage");
        Sprite rendered = batch.Sprites[0];
        Assert(rendered.X == 20 && rendered.Y == 41 && rendered.Width == 16 && rendered.Height == 45, "extraction uses composed world transform");
        Assert(rendered.R == 0.1f && rendered.G == 0.2f && rendered.B == 0.3f && rendered.A == 0.4f, "extraction preserves color and alpha");
        int capacity = batch.Capacity;
        world.Destroy(first);
        world.ExtractSprites(batch);
        Assert(batch.Count == 1 && batch.Capacity == capacity && batch.Sprites[0].Width == 8, "destroy compaction retains draw order and extraction reuses storage");
        second.Sprite = null;
        world.ExtractSprites(batch);
        Assert(batch.Count == 0, "empty extraction clears stale batch count");
        second.Sprite = new Sprite2D(float.MaxValue, 1);
        second.LocalTransform = new Transform2D(0, 0, 2, 1);
        parent.Sprite = new Sprite2D(1, 1);
        Throws<InvalidOperationException>(() => world.ExtractSprites(batch), "scaled extent overflow rejected");
        Assert(batch.Count == 0, "failed extraction discards the partial batch");

        World demo = DemoWorld.Create();
        var demoBatch = new SpriteBatch(DemoWorld.SpriteCount);
        var reference = new Sprite[DemoWorld.SpriteCount];
        demo.Update(1f / 60);
        demo.ExtractSprites(demoBatch);
        Program.Animate(reference, 1f / 60);
        Assert(demo.EntityCount == DemoWorld.SpriteCount && demoBatch.Count == DemoWorld.SpriteCount, "demo uses one ordinary entity per sprite");
        for (int i = 0; i < reference.Length; i++)
        {
            Sprite actual = demoBatch.Sprites[i], expected = reference[i];
            Assert(actual.X == expected.X && actual.Y == expected.Y && actual.Width == expected.Width
                && actual.Height == expected.Height && actual.R == expected.R && actual.G == expected.G
                && actual.B == expected.B && actual.A == expected.A, "world demo matches original sprite presentation");
        }

        const int warmupFrames = 128, measuredFrames = 1000;
        using var host = new EngineHost(true, DemoWorld.SpriteCount);
        var camera = new Camera { X = -480, Y = -270, Zoom = 1 };
        for (int i = 0; i < warmupFrames; i++) Frame();
        long start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < measuredFrames; i++) Frame();
        long bytes = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert(bytes == 0, $"world behavior/extract/draw loop allocated {bytes} bytes");
        Stats stats = host.GetStats();
        Assert(stats.Frames == warmupFrames + measuredFrames && stats.Sprites == (warmupFrames + measuredFrames) * DemoWorld.SpriteCount,
            "world batch crosses existing native ABI exactly once per frame");
        Console.WriteLine($"PASS world frame allocations bytes={bytes} frames={measuredFrames} entities=259 sprites_per_frame=259 warmup={warmupFrames}");

        void Frame()
        {
            Input input = host.Poll();
            Program.MoveCamera(ref camera, input, 1f / 60);
            demo.Update(1f / 60);
            demo.ExtractSprites(demoBatch);
            host.Draw(camera, demoBatch.Sprites);
        }
    }

    private sealed class Callback(Action<Entity, float> update) : IBehavior
    {
        public void Update(Entity entity, float deltaSeconds) => update(entity, deltaSeconds);
    }

    private sealed class TestBehaviorException : Exception;

    private static void Near(Transform2D actual, Transform2D expected, string message)
    {
        Assert(MathF.Abs(actual.X - expected.X) < 0.001f && MathF.Abs(actual.Y - expected.Y) < 0.001f
            && MathF.Abs(actual.ScaleX - expected.ScaleX) < 0.001f && MathF.Abs(actual.ScaleY - expected.ScaleY) < 0.001f, message);
    }

    private static void Throws<T>(Action action, string message) where T : Exception
    {
        bool threw = false;
        try { action(); }
        catch (T) { threw = true; }
        Assert(threw, message);
    }

    private static void Assert(bool condition, string message)
    {
        _assertions++;
        if (!condition) throw new InvalidOperationException($"WORLD SELF-TEST FAIL: {message}");
    }
}
