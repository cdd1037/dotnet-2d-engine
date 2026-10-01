namespace GameAuthoringLab;

// Runtime identity, not a serialization key. IDs are never reused, even by a
// different World in this process. Zero is always invalid.
internal readonly record struct EntityId(long Value);

// Radians and an explicit shear term keep the model closed under affine
// composition: rotating under nonuniform scale otherwise silently loses shape.
// Basis = rotation * [ScaleX Shear; 0 ScaleY]. Reflections remain unsupported.
internal readonly record struct Transform2D(float X, float Y, float ScaleX = 1, float ScaleY = 1,
    float Rotation = 0, float Shear = 0)
{
    public static Transform2D Identity => new(0, 0);

    internal void Validate()
    {
        if (!float.IsFinite(X) || !float.IsFinite(Y) || !float.IsFinite(ScaleX)
            || !float.IsFinite(ScaleY) || !float.IsFinite(Rotation) || !float.IsFinite(Shear)
            || ScaleX <= 0 || ScaleY <= 0)
            throw new ArgumentOutOfRangeException(nameof(Transform2D), "Transforms require finite values and positive scales.");
    }

    // Column basis: x' = M11*x + M21*y + X, y' = M12*x + M22*y + Y.
    public void GetBasis(out float m11, out float m12, out float m21, out float m22)
    {
        Affine2D matrix = ToAffine();
        m11 = (float)matrix.M11; m12 = (float)matrix.M12;
        m21 = (float)matrix.M21; m22 = (float)matrix.M22;
        if (!float.IsFinite(m11) || !float.IsFinite(m12) || !float.IsFinite(m21) || !float.IsFinite(m22))
            throw new ArgumentOutOfRangeException(nameof(Transform2D), "Affine basis exceeds float range.");
    }

    internal Affine2D ToAffine()
    {
        double cosine = Math.Cos(Rotation), sine = Math.Sin(Rotation);
        return new(cosine * ScaleX, sine * ScaleX,
            cosine * Shear - sine * ScaleY, sine * Shear + cosine * ScaleY, X, Y);
    }

    internal static Transform2D RelativeTo(in Transform2D world, in Transform2D parent)
        => Affine2D.RelativeTo(world.ToAffine(), parent.ToAffine()).ToTransform();
}

// Double intermediates are retained for an entire parent chain, then narrowed.
internal readonly record struct Affine2D(double M11, double M12, double M21, double M22, double X, double Y)
{
    internal static Affine2D Compose(in Affine2D parent, in Affine2D child) => new(
        parent.M11 * child.M11 + parent.M21 * child.M12,
        parent.M12 * child.M11 + parent.M22 * child.M12,
        parent.M11 * child.M21 + parent.M21 * child.M22,
        parent.M12 * child.M21 + parent.M22 * child.M22,
        parent.M11 * child.X + parent.M21 * child.Y + parent.X,
        parent.M12 * child.X + parent.M22 * child.Y + parent.Y);

    private static double DifferenceOfProducts(double a, double b, double c, double d)
    {
        double cd = c * d;
        return Math.FusedMultiplyAdd(a, b, -cd) + Math.FusedMultiplyAdd(-c, d, cd);
    }

    internal static Affine2D RelativeTo(in Affine2D world, in Affine2D parent)
    {
        double determinant = DifferenceOfProducts(parent.M11, parent.M22, parent.M12, parent.M21);
        if (!double.IsFinite(determinant) || determinant <= 0)
            throw new ArgumentOutOfRangeException(nameof(parent), "Parent affine basis is singular or outside numeric range.");
        double dx = world.X - parent.X, dy = world.Y - parent.Y;
        return new(
            DifferenceOfProducts(parent.M22, world.M11, parent.M21, world.M12) / determinant,
            DifferenceOfProducts(parent.M11, world.M12, parent.M12, world.M11) / determinant,
            DifferenceOfProducts(parent.M22, world.M21, parent.M21, world.M22) / determinant,
            DifferenceOfProducts(parent.M11, world.M22, parent.M12, world.M21) / determinant,
            DifferenceOfProducts(parent.M22, dx, parent.M21, dy) / determinant,
            DifferenceOfProducts(parent.M11, dy, parent.M12, dx) / determinant);
    }

    internal Transform2D ToTransform()
    {
        double sx = Math.Sqrt(M11 * M11 + M12 * M12);
        double determinant = DifferenceOfProducts(M11, M22, M12, M21);
        double sy = determinant / sx;
        double shear = (M11 * M21 + M12 * M22) / sx;
        var value = new Transform2D((float)X, (float)Y, (float)sx, (float)sy,
            (float)Math.Atan2(M12, M11), (float)shear);
        value.Validate();
        return value;
    }
}

internal readonly record struct Sprite2D(float Width, float Height, float R = 1, float G = 1, float B = 1, float A = 1,
    string? AssetKey = null, int Layer = 0, bool FlipX = false, bool FlipY = false)
{
    internal void Validate()
    {
        if (!float.IsFinite(Width) || !float.IsFinite(Height) || Width < 0 || Height < 0
            || !Unit(R) || !Unit(G) || !Unit(B) || !Unit(A)
            || (AssetKey is not null && string.IsNullOrWhiteSpace(AssetKey)))
            throw new ArgumentOutOfRangeException(nameof(Sprite2D), "Sprites require finite nonnegative extents, colors in [0, 1], and a nonempty optional asset key.");
    }

    private static bool Unit(float value) => float.IsFinite(value) && value is >= 0 and <= 1;
}

internal interface IBehavior
{
    void Update(Entity entity, float deltaSeconds);
}

internal sealed class Scene
{
    internal Scene(World world, string name, Guid persistentId) { World = world; Name = name; PersistentId = persistentId; }
    internal World World { get; }
    public string Name { get; }
    public Guid PersistentId { get; }
    public bool IsLoaded { get; internal set; } = true;
}

// An ordinary object with explicit properties, not a component registry/ECS.
internal sealed class Entity
{
    private Transform2D _localTransform;
    private Sprite2D? _sprite;
    private IBehavior? _behavior;
    internal BehaviorLifetime? Lifetime;
    internal void AssignBehavior(IBehavior? behavior)=>_behavior=behavior;

    internal Entity(World world, EntityId id, Guid persistentId, string name, Scene scene, Transform2D transform)
    {
        World = world; Id = id; PersistentId = persistentId; Name = name; SceneValue = scene; _localTransform = transform;
    }

    public World World { get; }
    public EntityId Id { get; }
    public Guid PersistentId { get; }
    public string Name { get; }
    public bool IsAlive { get; internal set; } = true;
    internal Entity? ParentValue, OwnerValue;
    internal Scene SceneValue;
    public Entity? TransformParent { get { CheckAlive(); return ParentValue; } }
    public Entity? LifetimeOwner { get { CheckAlive(); return OwnerValue; } }
    public Scene Scene { get { CheckAlive(); return SceneValue; } }

    public Transform2D LocalTransform
    {
        get { CheckAlive(); return _localTransform; }
        set { World.RequireMutationAllowed(); CheckAlive(); value.Validate(); _localTransform = value; }
    }

    public Transform2D WorldTransform => World.GetWorldTransform(this);

    public Sprite2D? Sprite
    {
        get { CheckAlive(); return _sprite; }
        set { World.RequireMutationAllowed(); CheckAlive(); value?.Validate(); _sprite = value; }
    }

    public IBehavior? Behavior
    {
        get { CheckAlive(); return _behavior; }
        set { World.SetBehavior(this,value); }
    }

    internal void CheckAlive()
    {
        if (!IsAlive) throw new InvalidOperationException($"Entity {Id.Value} ({Name}) has been destroyed.");
    }
}

internal sealed class World
{
    private static long _nextId;
    private readonly List<Entity> _entities = [];
    private readonly Dictionary<EntityId, Entity> _byId = [];
    private readonly Dictionary<Guid, Entity> _byPersistentId = [];
    private readonly Dictionary<Guid, Scene> _scenes = [];
    private bool _updating;
    private bool _lifecycleCallback;
    private bool _needsCompaction;

    public World(Guid? persistentSceneId = null)
    {
        Guid id = persistentSceneId ?? Guid.NewGuid();
        if (id == Guid.Empty) throw new ArgumentException("A persistent ID cannot be empty.", nameof(persistentSceneId));
        PersistentScene = new Scene(this, "Persistent", id);
        _scenes.Add(id, PersistentScene);
    }
    public Scene PersistentScene { get; }
    public int EntityCount => _byId.Count;

    public IEnumerable<Scene> LoadedScenes => _scenes.Values;
    public IReadOnlyList<Entity> Entities => _entities;

    public Scene CreateScene(string name, Guid? persistentId = null)
    {
        RequireMutationAllowed();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Guid id = AllocatePersistentId(persistentId);
        var scene = new Scene(this, name, id);
        _scenes.Add(id, scene);
        return scene;
    }

    public Scene GetScene(Guid id) => _scenes.TryGetValue(id, out Scene? scene) ? scene
        : throw new ArgumentException("Scene persistent ID is missing or unloaded.", nameof(id));

    public Entity GetPersistent(Guid id) => _byPersistentId.TryGetValue(id, out Entity? entity) ? entity
        : throw new ArgumentException("Entity persistent ID is missing or destroyed.", nameof(id));

    public bool TryGetPersistent(Guid id, out Entity? entity) => _byPersistentId.TryGetValue(id, out entity);

    // Objects without a specified room belong to the persistent world scope.
    public Entity Create(string name, Scene? scene = null, Transform2D? transform = null, Guid? persistentId = null)
    {
        RequireMutationAllowed();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        scene ??= PersistentScene;
        RequireScene(scene);
        Transform2D local = transform ?? Transform2D.Identity;
        local.Validate();
        Guid stableId = AllocatePersistentId(persistentId);
        long id = Interlocked.Increment(ref _nextId);
        if (id <= 0) throw new InvalidOperationException("Runtime entity ID space exhausted.");
        var entity = new Entity(this, new EntityId(id), stableId, name, scene, local);
        _entities.Add(entity);
        _byId.Add(entity.Id, entity);
        _byPersistentId.Add(stableId, entity);
        return entity;
    }

    // Convenience only: these three relationships can subsequently diverge.
    public Entity CreateChild(Entity parent, string name, Transform2D? transform = null)
    {
        RequireMutationAllowed();
        RequireEntity(parent);
        Entity child = Create(name, parent.SceneValue, transform);
        child.ParentValue = child.OwnerValue = parent;
        return child;
    }

    public bool TryGet(EntityId id, out Entity? entity) => _byId.TryGetValue(id, out entity);

    public Entity Get(EntityId id) => _byId.TryGetValue(id, out Entity? entity) ? entity
        : throw new ArgumentException("The entity ID is invalid, destroyed, or belongs to another world.", nameof(id));

    public Transform2D GetWorldTransform(Entity entity)
    {
        RequireEntity(entity);
        Affine2D accumulated = entity.LocalTransform.ToAffine();
        for (Entity? parent = entity.ParentValue; parent is not null; parent = parent.ParentValue)
            accumulated = Affine2D.Compose(parent.LocalTransform.ToAffine(), accumulated);
        return accumulated.ToTransform();
    }

    // Transform changes never implicitly change lifetime or scene membership.
    public void Reparent(Entity entity, Entity? parent, bool keepWorldTransform = true)
    {
        RequireMutationAllowed();
        RequireEntity(entity);
        ValidateRelation(entity, parent, ownership: false);
        Transform2D local = ReparentedLocal(entity, parent, keepWorldTransform);
        entity.ParentValue = parent;
        entity.LocalTransform = local;
    }

    public void SetOwner(Entity entity, Entity? owner)
    {
        RequireMutationAllowed();
        RequireEntity(entity);
        ValidateRelation(entity, owner, ownership: true);
        entity.OwnerValue = owner;
    }

    public void MoveToScene(Entity entity, Scene scene, bool includeOwned = false)
    {
        RequireMutationAllowed();
        RequireEntity(entity);
        RequireScene(scene);
        if (includeOwned)
            foreach (Entity member in OwnedSubtree(entity)) member.SceneValue = scene;
        else
            entity.SceneValue = scene;
    }

    // Explicit persistence includes owned equipment/children, but not merely
    // transform-parented neighbors. External relations are detached world-stably.
    public void MakePersistent(Entity entity)
    {
        RequireMutationAllowed();
        RequireEntity(entity);
        List<Entity> members = OwnedSubtree(entity);
        var retained = new HashSet<Entity>(members);
        // Resolve every transform first, so a numeric error cannot half-mutate.
        var locals = new Transform2D[members.Count];
        for (int i = 0; i < members.Count; i++)
            locals[i] = members[i].ParentValue is { } parent && !retained.Contains(parent)
                ? GetWorldTransform(members[i]) : members[i].LocalTransform;
        for (int i = 0; i < members.Count; i++)
        {
            Entity member = members[i];
            if (member.ParentValue is { } parent && !retained.Contains(parent)) member.ParentValue = null;
            if (member.OwnerValue is { } owner && !retained.Contains(owner)) member.OwnerValue = null;
            member.LocalTransform = locals[i];
            member.SceneValue = PersistentScene;
        }
    }

    // Convenience operations validate both graphs before changing any relation.
    // Pickup follows the carrier's scope; drop places the owned subtree in a room.
    public void PickUp(Entity entity, Entity carrier)
    {
        RequireMutationAllowed();
        RequireEntity(entity);
        RequireEntity(carrier);
        ValidateRelation(entity, carrier, ownership: false);
        ValidateRelation(entity, carrier, ownership: true);
        Transform2D local = ReparentedLocal(entity, carrier, keepWorldTransform: true);
        List<Entity> members = OwnedSubtree(entity);
        entity.ParentValue = entity.OwnerValue = carrier;
        entity.LocalTransform = local;
        foreach (Entity member in members) member.SceneValue = carrier.SceneValue;
    }

    public void Drop(Entity entity, Scene scene, Entity? transformParent = null)
    {
        RequireMutationAllowed();
        RequireEntity(entity);
        RequireScene(scene);
        ValidateRelation(entity, transformParent, ownership: false);
        Transform2D local = ReparentedLocal(entity, transformParent, keepWorldTransform: true);
        List<Entity> members = OwnedSubtree(entity);
        entity.ParentValue = transformParent;
        entity.OwnerValue = null;
        entity.LocalTransform = local;
        foreach (Entity member in members) member.SceneValue = scene;
    }

    // Explicit destruction follows lifetime ownership, regardless of scene.
    public void Destroy(Entity entity)
    {
        RequireMutationAllowed();
        RequireEntity(entity);
        DestroySet(new HashSet<Entity>(OwnedSubtree(entity)));
    }

    // Unloading starts with the room's members and follows ownership, stopping at
    // persistent members. Surviving external relations are safely detached.
    public void UnloadScene(Scene scene)
    {
        RequireMutationAllowed();
        RequireScene(scene);
        if (ReferenceEquals(scene, PersistentScene))
            throw new InvalidOperationException("The persistent world scope cannot be unloaded.");
        var doomed = new HashSet<Entity>();
        foreach (Entity entity in _entities)
            if (entity.IsAlive && ReferenceEquals(entity.SceneValue, scene)) doomed.Add(entity);
        bool changed;
        do
        {
            changed = false;
            foreach (Entity entity in _entities)
                if (entity.IsAlive && !ReferenceEquals(entity.SceneValue, PersistentScene)
                    && entity.OwnerValue is { } owner && doomed.Contains(owner))
                    changed |= doomed.Add(entity);
        } while (changed);
        DestroySet(doomed,scene);
    }

    public void Update(float deltaSeconds)
    {
        RequireMutationAllowed();
        if (!float.IsFinite(deltaSeconds) || deltaSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        if (_updating) throw new InvalidOperationException("World.Update is not reentrant.");
        _updating = true;
        try
        {
            // Newly created objects start updating next tick. Destroyed objects
            // are skipped; compaction waits until this pass has finished.
            int count = _entities.Count;
            for (int i = 0; i < count; i++)
            {
                Entity entity = _entities[i];
                if (entity.IsAlive) entity.Behavior?.Update(entity, deltaSeconds);
            }
        }
        finally { _updating = false; CompactDestroyed(); }
    }

    public void ExtractSprites(SpriteBatch batch)
    {
        RequireMutationAllowed();
        ArgumentNullException.ThrowIfNull(batch);
        batch.Reset(EntityCount);
        try
        {
            foreach (Entity entity in _entities)
            {
                if (!entity.IsAlive || entity.Sprite is not { } sprite) continue;
                Transform2D transform = GetWorldTransform(entity);
                batch.Add(transform, sprite);
            }
            batch.Sort();
        }
        catch
        {
            // A rejected extraction must not expose a plausible partial frame.
            batch.Reset(0);
            throw;
        }
    }

    private Guid AllocatePersistentId(Guid? requested)
    {
        Guid id = requested ?? Guid.NewGuid();
        if (id == Guid.Empty || _scenes.ContainsKey(id) || _byPersistentId.ContainsKey(id))
            throw new ArgumentException("Persistent IDs must be nonempty and unique within the world.", nameof(requested));
        return id;
    }

    private void RequireEntity(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (!ReferenceEquals(entity.World, this)) throw new ArgumentException("Entity belongs to another world.", nameof(entity));
        entity.CheckAlive();
    }

    private void RequireScene(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (!ReferenceEquals(scene.World, this) || !scene.IsLoaded)
            throw new ArgumentException("Scene is unloaded or belongs to another world.", nameof(scene));
    }

    private void ValidateRelation(Entity entity, Entity? target, bool ownership)
    {
        if (target is not null) RequireEntity(target);
        for (Entity? ancestor = target; ancestor is not null; ancestor = ownership ? ancestor.OwnerValue : ancestor.ParentValue)
            if (ReferenceEquals(ancestor, entity))
                throw new InvalidOperationException(ownership ? "Lifetime ownership cannot contain a cycle." : "Transform parenting cannot contain a cycle.");
    }

    private Transform2D ReparentedLocal(Entity entity, Entity? parent, bool keepWorldTransform)
    {
        if (!keepWorldTransform) return entity.LocalTransform;
        Transform2D world = GetWorldTransform(entity);
        return parent is null ? world : Transform2D.RelativeTo(world, GetWorldTransform(parent));
    }

    private List<Entity> OwnedSubtree(Entity root)
    {
        var result = new List<Entity> { root };
        for (int i = 0; i < result.Count; i++)
            foreach (Entity entity in _entities)
                if (entity.IsAlive && ReferenceEquals(entity.OwnerValue, result[i])) result.Add(entity);
        return result;
    }

    private void DestroySet(HashSet<Entity> doomed, Scene? unloading = null)
    {
        // Compute before mutating: survivors can depend on several doomed nodes.
        var detached = new List<(Entity Entity, Transform2D World)>();
        foreach (Entity entity in _entities)
            if (entity.IsAlive && !doomed.Contains(entity) && entity.ParentValue is { } parent && doomed.Contains(parent))
                detached.Add((entity, GetWorldTransform(entity)));
        var lifetimes = new List<BehaviorLifetime>();
        foreach (var (entity, world) in detached)
        {
            entity.ParentValue = null;
            entity.LocalTransform = world;
        }
        foreach (Entity entity in _entities)
        {
            if (!entity.IsAlive) continue;
            if (doomed.Contains(entity))
            {
                // Clear references before invalidating, to release user behaviors.
                entity.AssignBehavior(null);
                if(entity.Lifetime is {} lifetime){lifetimes.Add(lifetime);entity.Lifetime=null;}
                entity.ParentValue = entity.OwnerValue = null;
                entity.IsAlive = false;
                _byId.Remove(entity.Id);
                _byPersistentId.Remove(entity.PersistentId);
                _needsCompaction = true;
            }
            else if (entity.OwnerValue is { } owner && doomed.Contains(owner)) entity.OwnerValue = null;
        }
        if(unloading is not null){unloading.IsLoaded=false;_scenes.Remove(unloading.PersistentId);}
        if (!_updating) CompactDestroyed();
        List<Exception>? failures=null;
        _lifecycleCallback=true;
        try{foreach(var lifetime in lifetimes)lifetime.Release(ref failures);}
        finally{_lifecycleCallback=false;}
        if(failures is not null)throw new AggregateException("Destruction committed; behavior cleanup failed.",failures);
    }

    internal void RequireMutationAllowed()
    {
        if(_lifecycleCallback)throw new InvalidOperationException("World mutation/update/extraction is forbidden during behavior attach/detach callbacks.");
    }

    internal void SetBehavior(Entity entity,IBehavior? behavior)
    {
        RequireMutationAllowed();RequireEntity(entity);
        if(ReferenceEquals(entity.Behavior,behavior))return;
        var old=entity.Lifetime;entity.Lifetime=null;entity.AssignBehavior(behavior);
        ReleaseCommitted(old);
    }

    public void AttachBehavior(Entity entity,IBehavior behavior,Action<BehaviorLifetime> attach)
    {
        RequireMutationAllowed();RequireEntity(entity);ArgumentNullException.ThrowIfNull(behavior);ArgumentNullException.ThrowIfNull(attach);
        var candidate=new BehaviorLifetime();List<Exception>? failures=null;
        _lifecycleCallback=true;
        try
        {
            try{attach(candidate);candidate.Seal();}
            catch(Exception error){failures=[error];candidate.Release(ref failures);}
        }
        finally{_lifecycleCallback=false;}
        if(failures is not null)throw new AggregateException("Behavior attachment failed; previous behavior retained.",failures);
        var old=entity.Lifetime;entity.AssignBehavior(behavior);entity.Lifetime=candidate;
        ReleaseCommitted(old);
    }

    private void ReleaseCommitted(BehaviorLifetime? lifetime)
    {
        if(lifetime is null)return;
        List<Exception>? failures=null;_lifecycleCallback=true;
        try{lifetime.Release(ref failures);}finally{_lifecycleCallback=false;}
        if(failures is not null)throw new AggregateException("Behavior replacement committed; previous cleanup failed.",failures);
    }

    private void CompactDestroyed()
    {
        if (!_needsCompaction) return;
        int next = 0;
        for (int i = 0; i < _entities.Count; i++)
            if (_entities[i].IsAlive) _entities[next++] = _entities[i];
        _entities.RemoveRange(next, _entities.Count - next);
        _needsCompaction = false;
    }
}
