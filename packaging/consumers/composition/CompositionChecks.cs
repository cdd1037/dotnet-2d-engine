using GameAuthoringLab;

namespace CompositionSample;

internal static class CompositionChecks
{
    public static int Run()
    {
        int count = 0;
        void Check(bool condition, string why)
        {
            if (!condition) throw new InvalidOperationException("COMPOSITION: " + why);
            count++;
        }
        T Throws<T>(Action action, string why) where T : Exception
        {
            try { action(); }
            catch (T error) { count++; return error; }
            throw new InvalidOperationException("COMPOSITION: " + why);
        }

        var world = new World();
        var pulses = new PulseSource();
        var clip = new FrameClip(["enemy-a", "enemy-b"], .25);
        EnemySpec scout = new("enemy", new Transform2D(10, 20), 3, 8, 16, clip);
        EnemySpec guard = scout with { HitPoints = 7, Speed = 2, Size = 24, Placement = new Transform2D(40, 20) };
        using RoomInstance left = RoomFactory.Create(world, "same room name", new Transform2D(100, 50), [scout, guard], pulses);
        using RoomInstance right = RoomFactory.Create(world, "same room name", new Transform2D(600, 50), [scout, guard], pulses);
        EnemyInstance a = left.Enemies[0], b = left.Enemies[1], copy = right.Enemies[0];
        Check(world.EntityCount == 20 && pulses.Subscribers == 4, "two rooms contain four complete nested enemies");
        Check(a.Root != copy.Root && a.Root.Id != copy.Root.Id && a.Root.PersistentId != copy.Root.PersistentId
            && left.Scene.PersistentId != right.Scene.PersistentId && a.Root.Name == copy.Root.Name,
            "repeat names and definitions produce distinct instances and both identities");
        Check(a.State != copy.State && a.InitialBrain != copy.InitialBrain && a.Animation != copy.Animation
            && ReferenceEquals(a.Animation.Clip, copy.Animation.Clip), "mutable state is per instance; immutable clip is shared");
        Check(a.State.HitPoints == 3 && b.State.HitPoints == 7 && a.Visual.Sprite!.Value.Width == 16
            && b.Visual.Sprite!.Value.Width == 24, "factory parameters reach typed instance state and visuals");
        Check(a.Root.WorldTransform.X == 110 && copy.Root.WorldTransform.X == 610
            && a.Weapon.Blade.WorldTransform.X == 122, "nested room, enemy and weapon placements compose");

        a.State.Damage(1); a.Animation.Advance(TimingStep.FromReal(.3));
        Check(a.State.HitPoints == 2 && copy.State.HitPoints == 3 && a.Animation.FrameIndex == 1
            && copy.Animation.FrameIndex == 0, "damage and animation phase do not leak between repeated instances");
        pulses.Fire();
        Check(a.State.Pulses == 1 && copy.State.Pulses == 1 && b.State.Pulses == 1, "shared publisher reaches live instances");
        world.Update(.5f);
        Check(a.Root.LocalTransform.X == 14 && b.Root.LocalTransform.X == 41 && copy.Root.LocalTransform.X == 14,
            "parameterized behaviors update each instance independently");
        var replacement = new EnemyBrain(0);
        a.Root.Behavior = replacement;
        world.Update(.5f);
        Check(!a.Animation.IsDisposed && a.Root.LocalTransform.X == 14 && copy.Root.LocalTransform.X == 18
            && replacement.Ticks == 1 && pulses.Subscribers == 4, "AI replacement preserves instance-owned resources and subscription");

        // Geometry extraction needs no renderer; this is not a graphics acceptance claim.
        var batch = new SpriteBatch { RegionResolver = static _ => default }; // CPU-only placeholder, never submitted.
        world.ExtractSprites(batch);
        Check(batch.Count == 10, "two room floors and four visual/weapon pairs extract");
        a.Dispose(); a.Dispose();
        Check(!a.Root.IsAlive && !a.Visual.IsAlive && !a.Weapon.Blade.IsAlive && a.Animation.IsDisposed
            && b.Root.IsAlive && copy.Root.IsAlive && pulses.Subscribers == 3,
            "typed instance teardown destroys just its ownership subtree and is idempotent");
        Throws<ObjectDisposedException>(() => a.Animation.Advance(TimingStep.FromReal(.1)), "retired animation cannot advance");
        pulses.Fire();
        Check(a.State.Pulses == 1 && copy.State.Pulses == 2, "retired enemy receives no more external events");

        int before = world.EntityCount, scenesBefore = world.LoadedScenes.Count();
        int subscribersBefore = pulses.Subscribers;
        Throws<ArgumentOutOfRangeException>(() => EnemyFactory.Create(world, right.Root,
            scout with { Size = -1 }, pulses), "failure after acquisition rolls back enemy");
        Check(world.EntityCount == before && pulses.Subscribers == subscribersBefore && copy.Root.IsAlive,
            "failed enemy retains other live instances and releases its subscription");
        Throws<ArgumentOutOfRangeException>(() => RoomFactory.Create(world, "failed room", Transform2D.Identity,
            [scout, guard with { Size = -1 }], pulses), "partial room construction fails");
        Check(world.EntityCount == before && world.LoadedScenes.Count() == scenesBefore
            && pulses.Subscribers == subscribersBefore, "failed room removes successful earlier nested instances and scene");

        // Observe a real acquired operation during an ordinary scoped factory failure.
        FramePlayer? acquired = null;
        Throws<InvalidOperationException>(() => FailAfterAcquisition(world, right.Root, clip, value => acquired = value),
            "explicit acquisition failure expected");
        Check(acquired is { IsDisposed: true } && world.EntityCount == before,
            "failed factory releases real FramePlayer acquired before the failure");
        var aggregate = Throws<AggregateException>(() => FailAfterAcquisition(world, right.Root, clip,
            value => acquired = value, cleanupFails: true), "failed cleanup preserves construction error");
        Check(aggregate.InnerExceptions[0].Message == "construction failure"
            && aggregate.Flatten().InnerExceptions.Count == 2 && acquired!.IsDisposed && world.EntityCount == before,
            "construction and cleanup errors retained after committed rollback; remaining cleanup still runs");

        // Explicit persistence/ownership transfer carries resources with the same entity.
        world.MakePersistent(copy.Root);
        world.UnloadScene(right.Scene); right.Dispose();
        Check(copy.Root.IsAlive && copy.Weapon.Blade.IsAlive && !copy.Animation.IsDisposed
            && copy.Root.TransformParent is null && copy.Root.LifetimeOwner is null && pulses.Subscribers == 2,
            "persistent enemy escapes room unload explicitly and retains owned resources");
        copy.Dispose(); left.Dispose();
        Check(world.EntityCount == 0 && pulses.Subscribers == 0 && b.Animation.IsDisposed
            && world.LoadedScenes.Count() == 1, "room/world-first cleanup leaves no entities or subscriptions");
        Check(clip.FrameCount == 2, "borrowed shared definition remains usable after every instance is destroyed");

        for (int cycle = 0; cycle < 24; cycle++)
        {
            using var room = RoomFactory.Create(world, "repeated room", Transform2D.Identity, [scout, guard], pulses);
            var instances = room.Enemies;
            pulses.Fire(); world.Update(.1f);
            world.UnloadScene(room.Scene); // The external wrapper is disposed later.
            pulses.Fire();
            Check(world.EntityCount == 0 && pulses.Subscribers == 0 && world.LoadedScenes.Count() == 1
                && instances.All(enemy => enemy.Animation.IsDisposed && enemy.State.Pulses == 1),
                "repeated world-first unload retires instances before late wrapper disposal");
        }
        Console.WriteLine($"PACKAGE COMPOSITION PASS assertions={count} repeated_rooms=24 typed_factories=true native=false");
        return count;
    }

    // A small test factory, separate from game factories, so fault injection never
    // becomes an engine feature or a normal EnemySpec parameter.
    private static void FailAfterAcquisition(World world, Entity parent, FrameClip clip,
        Action<FramePlayer> acquired, bool cleanupFails = false)
    {
        Entity root = world.CreateChild(parent, "failing factory");
        try
        {
            var animation = new FramePlayer(clip);
            try { world.OnDestroy(root, animation.Dispose); }
            catch { animation.Dispose(); throw; }
            acquired(animation);
            if (cleanupFails) world.OnDestroy(root, () => throw new InvalidOperationException("cleanup failure"));
            world.CreateChild(root, "partial child");
            throw new InvalidOperationException("construction failure");
        }
        catch (Exception construction)
        {
            try { if (root.IsAlive) world.Destroy(root); }
            catch (Exception cleanup) { throw new AggregateException("Factory and cleanup failed.", construction, cleanup); }
            throw;
        }
    }
}
