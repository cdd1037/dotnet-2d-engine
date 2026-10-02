using GameAuthoringLab;

namespace CompositionSample;

// These are GAME-AUTHORED types. The engine has no enemy/room schema, factory
// registry, inheritance tree, role lookup, or automatic component construction.
internal sealed record EnemySpec(string Name, Transform2D Placement, int HitPoints,
    float Speed, float Size, FrameClip Animation);

internal sealed class PulseSource
{
    public event Action? Pulse;
    public int Subscribers => Pulse?.GetInvocationList().Length ?? 0;
    public void Fire() => Pulse?.Invoke();
}

internal sealed class EnemyState(int hitPoints)
{
    public int HitPoints { get; private set; } = hitPoints;
    public int Pulses { get; private set; }
    public void Damage(int amount)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        HitPoints = Math.Max(0, HitPoints - amount);
    }
    public void OnPulse() => Pulses++;
}

internal sealed class EnemyBrain(float speed) : IBehavior
{
    public int Ticks { get; private set; }
    public void Update(Entity entity, float deltaSeconds)
    {
        Ticks++;
        var local = entity.LocalTransform;
        entity.LocalTransform = local with { X = local.X + speed * deltaSeconds };
    }
}

internal sealed class WeaponInstance(Entity root, Entity blade) : IDisposable
{
    public Entity Root { get; } = root;
    public Entity Blade { get; } = blade;
    public void Dispose() { if (Root.IsAlive) Root.World.Destroy(Root); }
}

internal sealed class EnemyInstance(Entity root, Entity visual, WeaponInstance weapon,
    EnemyState state, EnemyBrain brain, FramePlayer animation) : IDisposable
{
    public Entity Root { get; } = root;
    public Entity Visual { get; } = visual;
    public WeaponInstance Weapon { get; } = weapon;
    public EnemyState State { get; } = state;
    public EnemyBrain InitialBrain { get; } = brain;
    public FramePlayer Animation { get; } = animation;
    public void Dispose() { if (Root.IsAlive) Root.World.Destroy(Root); }
}

internal sealed class RoomInstance(Scene scene, Entity root, Entity floor,
    IReadOnlyList<EnemyInstance> enemies) : IDisposable
{
    public Scene Scene { get; } = scene;
    public Entity Root { get; } = root;
    public Entity Floor { get; } = floor;
    public IReadOnlyList<EnemyInstance> Enemies { get; } = enemies;
    public void Dispose() { if (Scene.IsLoaded) Root.World.UnloadScene(Scene); }
}

internal static class WeaponFactory
{
    public static WeaponInstance Create(World world, Entity owner, float length)
    {
        Entity root = world.CreateChild(owner, "weapon", new Transform2D(12, 0));
        try
        {
            Entity blade = world.CreateChild(root, "blade");
            blade.Sprite = new(length, 3, R: .9f, G: .8f, B: .2f, Layer: 3);
            return new(root, blade);
        }
        catch (Exception construction)
        {
            try { if (root.IsAlive) world.Destroy(root); }
            catch (Exception cleanup) { throw new AggregateException("Weapon construction and cleanup failed.", construction, cleanup); }
            throw;
        }
    }
}

internal static class EnemyFactory
{
    public static EnemyInstance Create(World world, Entity parent, EnemySpec spec, PulseSource pulses)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(pulses);
        ArgumentNullException.ThrowIfNull(spec.Animation);
        if (spec.HitPoints < 1 || !float.IsFinite(spec.Speed)) throw new ArgumentOutOfRangeException(nameof(spec));
        Entity root = world.CreateChild(parent, spec.Name, spec.Placement);
        try
        {
            // A fresh player belongs to this instance; its immutable clip is borrowed.
            var animation = new FramePlayer(spec.Animation);
            try { world.OnDestroy(root, animation.Dispose); }
            catch { animation.Dispose(); throw; } // Ownership transfers only after registration succeeds.

            var state = new EnemyState(spec.HitPoints);
            Action handler = state.OnPulse;
            world.OnDestroy(root, () => pulses.Pulse -= handler); // Undo before fallible subscription setup.
            pulses.Pulse += handler;

            Entity visual = world.CreateChild(root, "visual");
            // Engine value validation can reject this after resources were acquired.
            visual.Sprite = new(spec.Size, spec.Size, AssetKey: animation.AssetKey, Layer: 2);
            WeaponInstance weapon = WeaponFactory.Create(world, root, spec.Size / 2);
            var brain = new EnemyBrain(spec.Speed);
            root.Behavior = brain;
            return new(root, visual, weapon, state, brain, animation);
        }
        catch (Exception construction)
        {
            try { if (root.IsAlive) world.Destroy(root); }
            catch (Exception cleanup) { throw new AggregateException("Enemy construction and cleanup failed.", construction, cleanup); }
            throw;
        }
    }
}

internal static class RoomFactory
{
    public static RoomInstance Create(World world, string name, Transform2D placement,
        ReadOnlySpan<EnemySpec> enemies, PulseSource pulses)
    {
        Scene scene = world.CreateScene(name);
        try
        {
            Entity root = world.Create("room", scene, placement);
            Entity floor = world.CreateChild(root, "floor");
            floor.Sprite = new(320, 180, R: .1f, G: .2f, B: .3f, Layer: -1);
            var instances = new EnemyInstance[enemies.Length];
            for (int i = 0; i < enemies.Length; i++) instances[i] = EnemyFactory.Create(world, root, enemies[i], pulses);
            return new(scene, root, floor, Array.AsReadOnly(instances));
        }
        catch (Exception construction)
        {
            try { if (scene.IsLoaded) world.UnloadScene(scene); }
            catch (Exception cleanup) { throw new AggregateException("Room construction and cleanup failed.", construction, cleanup); }
            throw;
        }
    }
}
