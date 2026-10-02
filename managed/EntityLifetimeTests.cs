namespace GameAuthoringLab;

internal static class EntityLifetimeTests
{
    public static int Run()
    {
        int count = 0;
        void Check(bool condition, string why)
        {
            if (!condition) throw new InvalidOperationException("ENTITY LIFETIME: " + why);
            count++;
        }
        T Throws<T>(Action action, string why) where T : Exception
        {
            try { action(); }
            catch (T error) { count++; return error; }
            throw new InvalidOperationException("ENTITY LIFETIME: " + why);
        }

        var world = new World();
        var room = world.CreateScene("room");
        var entity = world.Create("enemy", room);
        int destroyed = 0, detached = 0;
        world.OnDestroy(entity, () => destroyed++);
        var behavior = new TickBehavior();
        world.AttachBehavior(entity, behavior, lifetime => lifetime.OnDetach(() => detached++));
        entity.Behavior = behavior;
        Check(detached == 0 && destroyed == 0, "same behavior leaves both lifetimes alone");
        entity.Behavior = new TickBehavior();
        Check(detached == 1 && destroyed == 0, "behavior replacement does not release instance ownership");
        entity.Behavior = null;
        Throws<AggregateException>(() => world.AttachBehavior(entity, behavior,
            _ => throw new InvalidOperationException("candidate")), "failed attachment expected");
        Check(entity.Behavior is null && destroyed == 0, "failed attachment retains destruction registration");
        world.UnloadScene(room);
        Check(destroyed == 1 && detached == 1 && !entity.IsAlive, "entity cleanup works without a behavior");
        Throws<InvalidOperationException>(() => world.Destroy(entity), "ordinary repeated Destroy stays rejected");
        Check(destroyed == 1, "repeat destruction cannot invoke registration twice");

        entity = world.Create("validation");
        var foreign = new World().Create("foreign");
        Throws<ArgumentNullException>(() => world.OnDestroy(null!, () => { }), "null entity");
        Throws<ArgumentException>(() => world.OnDestroy(foreign, () => { }), "foreign entity");
        Throws<ArgumentNullException>(() => world.OnDestroy(entity, null!), "null cleanup");
        world.Destroy(entity);
        Throws<InvalidOperationException>(() => world.OnDestroy(entity, () => { }), "dead entity");

        // Ownership can run opposite to creation order and is not transform parentage.
        world = new World(); room = world.CreateScene("ordering");
        var child = world.Create("older child", room);
        var parent = world.Create("newer owner", room);
        world.SetOwner(child, parent);
        world.Reparent(parent, child, keepWorldTransform: false);
        var order = new List<string>();
        world.AttachBehavior(child, new TickBehavior(), s => s.OnDetach(() => order.Add("child behavior")));
        world.AttachBehavior(parent, new TickBehavior(), s => s.OnDetach(() => order.Add("parent behavior")));
        world.OnDestroy(parent, () => order.Add("parent resource"));
        world.OnDestroy(child, () => order.Add("child first"));
        world.OnDestroy(child, () => order.Add("child last"));
        world.Destroy(parent);
        Check(string.Join(",", order) == "child behavior,parent behavior,child last,child first,parent resource",
            "behavior order retained, ownership descendants first, registration LIFO");

        world = new World(); parent = world.Create("older owner"); child = world.CreateChild(parent, "newer child");
        order.Clear();
        world.AttachBehavior(parent, new TickBehavior(), s => s.OnDetach(() => order.Add("parent behavior")));
        world.AttachBehavior(child, new TickBehavior(), s => s.OnDetach(() => order.Add("child behavior")));
        world.OnDestroy(parent, () => order.Add("parent resource"));
        world.OnDestroy(child, () => order.Add("child resource"));
        world.Destroy(parent);
        Check(string.Join(",", order) == "parent behavior,child behavior,child resource,parent resource",
            "behavior and destruction phases intentionally use distinct ordering");

        // An unload can leave a persistent intermediary alive between two doomed nodes.
        world = new World(); room = world.CreateScene("persistent gap");
        parent = world.Create("ancestor", room);
        var survivor = world.Create("persistent intermediary");
        child = world.Create("descendant", room);
        world.SetOwner(survivor, parent); world.SetOwner(child, survivor);
        order.Clear();
        world.OnDestroy(parent, () => order.Add("ancestor"));
        world.OnDestroy(survivor, () => order.Add("survivor"));
        world.OnDestroy(child, () => order.Add("descendant"));
        world.UnloadScene(room);
        Check(string.Join(",", order) == "descendant,ancestor" && survivor.IsAlive,
            "full precommit ancestry orders through a surviving persistent node");
        Check(survivor.LifetimeOwner is null, "persistent survivor detaches owner");
        world.Destroy(survivor);
        Check(order[^1] == "survivor" && order.Count == 3, "persistent survivor retains its own registration");

        world = new World(); room = world.CreateScene("committed errors");
        parent = world.Create("parent", room, new Transform2D(20, 30));
        child = world.CreateChild(parent, "child");
        survivor = world.Create("survivor", transform: new Transform2D(4, 5));
        world.Reparent(survivor, parent, false);
        var survivingTransform = survivor.WorldTransform;
        var originalParent = parent; var originalChild = child; var originalRoom = room;
        int observed = 0, attempted = 0, blocked = 0;
        Action observe = () =>
        {
            Check(!originalParent.IsAlive && !originalChild.IsAlive && !originalRoom.IsLoaded
                && !world.TryGet(originalParent.Id, out _) && !world.TryGetPersistent(originalChild.PersistentId, out _)
                && survivor.IsAlive && survivor.TransformParent is null && survivor.WorldTransform == survivingTransform,
                "cleanup sees entire committed world and stable survivor");
            observed++;
        };
        world.AttachBehavior(parent, new TickBehavior(), s => s.OnDetach(() =>
        { observe(); throw new InvalidOperationException("behavior error"); }));
        world.OnDestroy(parent, () => { observe(); attempted++; throw new InvalidOperationException("parent error"); });
        world.OnDestroy(child, () => { observe(); attempted++; });
        world.OnDestroy(child, () => { attempted++; throw new InvalidOperationException("child error"); });
        world.OnDestroy(child, () =>
        {
            Action[] mutations = [() => world.Create("illegal"), () => world.CreateScene("illegal"),
                () => world.Update(0), () => world.ExtractSprites(new SpriteBatch()),
                () => world.OnDestroy(survivor, () => { }), () => world.Destroy(survivor),
                () => survivor.Behavior = null, () => survivor.Sprite = null,
                () => survivor.LocalTransform = Transform2D.Identity,
                () => world.SetOwner(survivor, null), () => world.MoveToScene(survivor, world.PersistentScene)];
            foreach (var mutation in mutations)
                try { mutation(); } catch (InvalidOperationException) { blocked++; }
            var unrelated = new World(); unrelated.Destroy(unrelated.Create("allowed"));
        });
        var failure = Throws<AggregateException>(() => world.UnloadScene(room), "all cleanup failures aggregated");
        Check(failure.InnerExceptions.Count == 3 && observed == 3 && attempted == 3 && blocked == 11,
            "all cleanup phases attempted and same-world reentrancy blocked");
        Check(world.Create("after errors").IsAlive, "mutation guard recovered after errors");

        // Numeric preflight must retain every registration for a repaired retry.
        world = new World(); room = world.CreateScene("overflow");
        parent = world.Create("parent", room, new Transform2D(0, 0, 1e30f, 1e30f));
        survivor = world.Create("survivor", transform: new Transform2D(0, 0, 1e30f, 1e30f));
        world.Reparent(survivor, parent, false);
        destroyed = 0;
        world.OnDestroy(parent, () => destroyed++);
        Throws<ArgumentOutOfRangeException>(() => world.UnloadScene(room), "overflow preflight expected");
        Check(destroyed == 0 && parent.IsAlive && room.IsLoaded, "failed preflight retains entity resources");
        survivor.LocalTransform = Transform2D.Identity;
        world.UnloadScene(room);
        Check(destroyed == 1 && !parent.IsAlive && survivor.IsAlive, "repaired teardown releases once");

        world = new World(); room = world.CreateScene("moving");
        var carrier = world.Create("carrier");
        var item = world.Create("item", room);
        destroyed = 0;
        world.OnDestroy(item, () => destroyed++);
        world.PickUp(item, carrier); world.UnloadScene(room);
        Check(item.IsAlive && destroyed == 0, "pickup transfers registered lifetime with entity");
        world.Destroy(carrier);
        Check(destroyed == 1 && !item.IsAlive, "explicit owner destruction includes persistent item resources");

        world = new World();
        var actor = world.Create("actor"); var later = world.Create("later");
        var laterBehavior = new TickBehavior(); later.Behavior = laterBehavior;
        actor.Behavior = new TickBehavior(() => world.Destroy(actor));
        world.OnDestroy(actor, () => throw new InvalidOperationException("update teardown"));
        Throws<AggregateException>(() => world.Update(.1f), "destruction during update propagates cleanup error");
        Check(!actor.IsAlive && world.EntityCount == 1 && laterBehavior.Ticks == 0,
            "exceptional update compacts and does not pretend later entities updated");
        world.Update(.1f);
        Check(laterBehavior.Ticks == 1, "update recovers after entity cleanup error");

        world = new World();
        entity = world.Create("duplicates"); destroyed = 0;
        Action duplicate = () => destroyed++;
        world.OnDestroy(entity, duplicate); world.OnDestroy(entity, duplicate);
        world.Destroy(entity);
        Check(destroyed == 2, "once means once per explicit registration, without deduplication");
        entity = world.Create("attach guard");
        Throws<AggregateException>(() => world.AttachBehavior(entity, new TickBehavior(),
            _ => world.OnDestroy(entity, () => destroyed++)), "cannot register entity cleanup from attach callback");
        world.Destroy(entity);
        Check(destroyed == 2, "rejected registration adds no callback");

        // This ordering path must not recurse through arbitrary runtime hierarchy depth.
        world = new World(); parent = world.Create("deep root"); child = parent; order.Clear();
        world.OnDestroy(parent, () => order.Add("root"));
        for (int i = 0; i < 2048; i++) child = world.CreateChild(child, "nested");
        world.OnDestroy(child, () => order.Add("leaf"));
        world.Destroy(parent);
        Check(string.Join(",", order) == "leaf,root" && world.EntityCount == 0, "iterative deep ownership cleanup");
        Console.WriteLine($"PASS entity destruction ownership ({count} assertions)");
        return count;
    }

    private sealed class TickBehavior(Action? action = null) : IBehavior
    {
        public int Ticks { get; private set; }
        public void Update(Entity entity, float deltaSeconds) { Ticks++; action?.Invoke(); }
    }
}
