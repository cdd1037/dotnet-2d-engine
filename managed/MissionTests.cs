using System.Text.Json.Nodes;

namespace GameAuthoringLab;

internal static class MissionTests
{
    public static int Run()
    {
        int count = 0;
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException("MISSION: " + message); count++; }
        void Reject(Action action, string message) { try { action(); } catch (Exception e) when (e is SceneFormatException or UiAuthoringException or IOException or InvalidOperationException) { count++; return; } throw new Exception("MISSION accepted " + message); }
        string directory = Path.Combine(Path.GetTempPath(), "gal-mission-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var catalog = SampleAssets.Catalog();
        string path = Path.Combine(directory, "relay.mission.json");
        string source = File.ReadAllText(Path.Combine(catalog.Root, "relay.mission.json"));
        File.WriteAllText(path, source);
        try
        {
            using var game = new MissionGame(path, catalog);
            Check(game.Screen == MissionScreen.Title && game.Room.World.EntityCount == 6, "title has one frozen room");
            var titlePosition = game.Room.Player.LocalTransform;
            game.Advance(Native.Right, 10);
            Check(game.Room.Player.LocalTransform == titlePosition, "title does not simulate");
            var titleWorld = game.Room.World;
            game.Start();
            Check(titleWorld.EntityCount == 0 && game.Screen == MissionScreen.Playing, "start retires title world");
            var start = game.Room.Player.LocalTransform;
            game.Advance(Native.Right | Native.Interact, 10);
            Check(game.Room.Player.LocalTransform == start && game.RemainingTicks == 5400, "start holds all input until neutral");
            game.Advance(0, 10); game.Advance(Native.Right, RoomGame.FixedDelta);
            Check(game.Room.Player.LocalTransform.X == start.X + 3 && game.RemainingTicks == 5399, "one fresh tick after start");
            game.Room.Player.LocalTransform = new Transform2D(230, 280);
            game.Room.Advance(Native.Interact, RoomGame.FixedDelta / 4);
            int ticks = game.RemainingTicks;
            game.Pause();
            for (int i = 0; i < 20; i++) game.Advance(Native.Right | Native.Interact, 1);
            Check(game.RemainingTicks == ticks && game.Room.Held is null, "pause freezes timer and pending pickup");
            game.Resume(); game.Advance(Native.Interact, 1); game.Advance(0, 1); game.Advance(0, RoomGame.FixedDelta);
            Check(game.Room.Held is null && game.RemainingTicks == ticks - 1, "resume clears pending edge and elapsed backlog");
            game.Advance(Native.Interact, RoomGame.FixedDelta);
            Check(game.Room.Held == game.Room.Item, "normal pickup");
            game.Room.Player.LocalTransform = new Transform2D(830, 280);
            Scene firstRoom = game.Room.ActiveScene;
            game.Advance(Native.Transition, RoomGame.FixedDelta);
            Check(game.Room.RoomIndex == 1 && !firstRoom.IsLoaded && game.Room.Held is not null, "cross room carrying cell");
            string checkpoint = game.Save(); var oldWorld = game.Room.World; var playerId = game.Room.Player.PersistentId;
            int savedTicks = game.RemainingTicks;
            game.Load(checkpoint);
            Check(oldWorld.EntityCount == 0 && game.Room.Player.PersistentId == playerId && game.Screen == MissionScreen.Paused && game.RemainingTicks == savedTicks, "load swaps fresh world preserving progress and enters pause");
            game.Resume(); game.Advance(0, 0);
            game.Room.Player.LocalTransform = new Transform2D(game.Definition.DeliveryX, game.Definition.DeliveryY);
            game.Advance(0, RoomGame.FixedDelta);
            Check(game.Screen == MissionScreen.Playing, "arrival alone does not deliver");
            game.Advance(Native.Interact, RoomGame.FixedDelta);
            Check(game.Screen == MissionScreen.Won, "E delivers in target zone");
            int terminalTicks = game.RemainingTicks; game.Advance(Native.Right, 1);
            Check(game.RemainingTicks == terminalTicks, "won state freezes");
            Reject(() => game.Save(), "save terminal state");
            game.Start(); game.Advance(0, 0);
            for (int i = 0; i < 5400; i++) game.Advance(0, RoomGame.FixedDelta);
            Check(game.Screen == MissionScreen.Lost && game.RemainingTicks == 0, "time limit produces loss");
            game.Start(); game.Advance(0, 0);
            var current = game.Room; ticks = game.RemainingTicks;
            Reject(() => game.Load("{"), "malformed save");
            Check(ReferenceEquals(current, game.Room) && game.RemainingTicks == ticks, "malformed load leaves last usable state");
            var json = JsonNode.Parse(game.Save())!.AsObject(); json["remainingTicks"] = -1;
            Reject(() => game.Load(json.ToJsonString()), "negative time");
            json["remainingTicks"] = 1; json["unexpected"] = true;
            Reject(() => game.Load(json.ToJsonString()), "unknown save field");
            Reject(() => game.Load(game.Save().Replace("\"version\": 1", "\"version\": 1, \"version\": 1", StringComparison.Ordinal)), "duplicate envelope field");
            File.WriteAllText(path, source.Replace("\"seconds\": 90", "\"seconds\": 91", StringComparison.Ordinal));
            Reject(() => game.Load(checkpoint), "changed mission rules");
            Check(ReferenceEquals(current, game.Room), "incompatible mission keeps current run");
            File.WriteAllText(path, source.Replace("\"deliveryX\": 800", "\"deliveryX\": 400", StringComparison.Ordinal));
            Reject(() => game.Start(), "invalid authored target");
            Check(ReferenceEquals(current, game.Room), "invalid authored restart keeps current run");
            File.WriteAllText(path, source);
            RoomGame? rejectedCandidate = null;
            Reject(() => game.Start(candidate => { rejectedCandidate = candidate; throw new IOException("simulated resource upload failure"); }), "candidate preparation failure");
            Check(ReferenceEquals(current, game.Room) && rejectedCandidate!.World.EntityCount == 0, "failed prepare retires candidate only");
            // A loaded one-tick clock cannot simulate eight ticks before losing.
            var lastTickSave = JsonNode.Parse(game.Save())!.AsObject(); lastTickSave["remainingTicks"] = 1;
            game.Load(lastTickSave.ToJsonString()); game.Resume(); game.Advance(0, 0);
            float lastX = game.Room.Player.LocalTransform.X;
            game.Advance(Native.Right, .25f);
            Check(game.Screen == MissionScreen.Lost && game.Room.Player.LocalTransform.X == lastX + 3, "deadline caps the final simulation batch");
            game.Start(); game.Advance(0, 0);
            string savePath = Path.Combine(directory, "checkpoint.json");
            game.SaveFile(savePath); string previousBytes = File.ReadAllText(savePath);
            game.Title(); Reject(() => game.SaveFile(savePath), "title overwrite");
            Check(File.ReadAllText(savePath) == previousBytes && Directory.GetFiles(directory, "*.tmp").Length == 0, "failed save retains destination and leaves no temp");
            game.LoadFile(savePath); Check(game.Screen == MissionScreen.Paused, "file reload works");
            int subscribers = 0;
            for (int cycle = 0; cycle < 25; cycle++)
            {
                game.Start(); var world = game.Room.World; var room = game.Room.ActiveScene;
                game.Room.World.AttachBehavior(game.Room.Player, new IdleBehavior(), lifetime =>
                { subscribers++; lifetime.OnDetach(() => subscribers--); });
                game.Room.Player.LocalTransform = new Transform2D(230, 280); game.Room.PickUp();
                for (int crossing = 0; crossing < 4; crossing++)
                {
                    game.Room.Player.LocalTransform = new Transform2D(game.Room.RoomIndex == 0 ? 830 : 110, 280);
                    Check(game.Room.UseDoor() && game.Room.World.EntityCount == 6, "transition keeps bounded entity count");
                }
                var firstDefinition = game.Definition;
                game.Start();
                Check(world.EntityCount == 0 && !room.IsLoaded && subscribers == 0, "restart detaches all owned behavior and retires entities/scenes");
                Check(game.Room.World.EntityCount == 6 && game.Definition == firstDefinition && !ReferenceEquals(game.Definition, firstDefinition), "same authored data yields independent run");
            }
            string assetDirectory = Path.Combine(directory, "assets"); Directory.CreateDirectory(assetDirectory);
            foreach (string asset in Directory.GetFiles(catalog.Root, "*.bmp")) File.Copy(asset, Path.Combine(assetDirectory, Path.GetFileName(asset)));
            using (var isolated = new MissionGame(path, SampleAssets.Catalog(assetDirectory)))
            {
                var usable = isolated.Room;
                File.Delete(Path.Combine(assetDirectory, "room-b.bmp"));
                Reject(() => isolated.Start(), "missing destination asset");
                Check(ReferenceEquals(usable, isolated.Room) && usable.World.EntityCount == 6, "missing resource preserves usable world");
                File.WriteAllText(Path.Combine(assetDirectory, "room-b.bmp"), "bad BMP");
                Reject(() => isolated.Start(), "invalid destination asset");
                Check(ReferenceEquals(usable, isolated.Room), "invalid resource preserves usable world");
            }
            count += RetirementFailures(path, catalog);
            game.Dispose(); game.Dispose();
            Check(game.Room.World.EntityCount == 0 && subscribers == 0, "final idempotent disposal");
            Console.WriteLine($"MISSION SELF-TEST PASS assertions={count}");
            return count;
        }
        finally { Directory.Delete(directory, true); }
    }

    private static int RetirementFailures(string path, AssetCatalog catalog)
    {
        int count = 0;
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException("MISSION: " + message); count++; }
        T Throws<T>(Action action, string message) where T : Exception
        {
            try { action(); }
            catch (T error) { count++; return error; }
            throw new InvalidOperationException("MISSION accepted " + message);
        }
        (Entity Parent, Entity Leaf) InvalidRetirement(RoomGame room)
        {
            Entity parent = room.World.Entities[0];
            parent.LocalTransform = new Transform2D(0, 0, 1e30f, 1e30f);
            room.Player.LocalTransform = new Transform2D(0, 0, 1e30f, 1e30f);
            room.World.Reparent(room.Player, parent, keepWorldTransform: false);
            Entity leaf = room.World.Create("Overflow leaf");
            room.World.Reparent(leaf, room.Player, keepWorldTransform: false);
            return (parent, leaf);
        }

        using (var game = new MissionGame(path, catalog))
        {
            game.Start();
            string saved = game.Save();
            var room = game.Room;
            var extraScene = room.World.CreateScene("Additional cleanup");
            var extra = room.World.Create("Additional entity", extraScene);
            Entity[] entities = room.World.Entities.ToArray();
            int sceneCleanups = 0, persistentCleanups = 0, signalHits = 0;
            Action? signal = null;
            Action handler = () => signalHits++;
            signal += handler; signal();
            var roomFailure = new InvalidOperationException("room detach");
            var extraFailure = new InvalidOperationException("extra scene detach");
            var playerFailure = new InvalidOperationException("player detach");
            room.World.AttachBehavior(room.Item, new IdleBehavior(), scope => scope.OnDetach(() =>
            {
                sceneCleanups++;
                game.Dispose(); // The recursive call must not enter retirement again.
                Throws<ObjectDisposedException>(() => game.Load(saved), "load from disposal callback");
                Throws<ObjectDisposedException>(() => game.Start(), "start from disposal callback");
                throw roomFailure;
            }));
            room.World.AttachBehavior(extra, new IdleBehavior(), scope => scope.OnDetach(() => { sceneCleanups++; throw extraFailure; }));
            room.World.AttachBehavior(room.Player, new IdleBehavior(), scope => scope.OnDetach(() =>
            { persistentCleanups++; signal -= handler; throw playerFailure; }));
            room.World.AttachBehavior(entities.Single(entity => entity.Name == "Status"), new IdleBehavior(),
                scope => scope.OnDetach(() => persistentCleanups++));

            var failures = Throws<AggregateException>(() => game.Dispose(), "disposal cleanup errors").Flatten().InnerExceptions;
            Check(failures.Count == 3 && failures.Contains(roomFailure) && failures.Contains(extraFailure) && failures.Contains(playerFailure),
                "disposal reports all scene and persistent cleanup failures");
            signal?.Invoke();
            Check(sceneCleanups == 2 && persistentCleanups == 2 && signal is null && signalHits == 1,
                "throwing cleanup cannot skip later scenes, persistent entities or subscription removal");
            Check(room.World.EntityCount == 0 && room.World.Entities.Count == 0 && entities.All(entity => !entity.IsAlive)
                && !room.ActiveScene.IsLoaded && !extraScene.IsLoaded && room.World.LoadedScenes.Single() == room.World.PersistentScene,
                "failed callbacks leave all destruction committed");
            game.Dispose(); game.Dispose();
            Check(sceneCleanups == 2 && persistentCleanups == 2, "repeated disposal never repeats cleanup callbacks");

            int prepares = 0, revision = game.Revision;
            string notice = game.Notice;
            Action[] closedOperations = [() => game.Start(_ => prepares++), () => game.Load(saved, _ => prepares++),
                () => game.Load("{"), () => game.LoadFile(Path.Combine(path, "missing.json")),
                () => game.Pause(), () => game.Resume(), () => game.Title(), () => game.SetNotice("closed"),
                () => game.Advance(0, 0), () => game.Advance(0, 0, 0), () => game.Save(),
                () => game.SaveFile(Path.Combine(path, "missing.json"))];
            foreach (var operation in closedOperations)
                Throws<ObjectDisposedException>(operation, "operation on disposed mission");
            Check(prepares == 0 && ReferenceEquals(game.Room, room) && room.World.EntityCount == 0
                && game.Revision == revision && game.Notice == notice, "closed operations reject before preparation, I/O or state changes");
        }

        using (var game = new MissionGame(path, catalog))
        {
            game.Start(); string saved = game.Save();
            var room = game.Room;
            var (parent, leaf) = InvalidRetirement(room);
            int releases = 0;
            room.World.AttachBehavior(parent, new IdleBehavior(), scope => scope.OnDetach(() => releases++));
            room.World.AttachBehavior(room.Player, new IdleBehavior(), scope => scope.OnDetach(() => releases++));
            var errors = Throws<AggregateException>(() => game.Dispose(), "numeric retirement preflight").Flatten().InnerExceptions;
            Check(errors.All(error => error is ArgumentOutOfRangeException) && parent.IsAlive && room.Player.IsAlive
                && room.ActiveScene.IsLoaded && room.World.EntityCount == 2 && !leaf.IsAlive && releases == 0,
                "preflight failures are bounded, retain invalid survivors and still attempt unrelated cleanup");
            Throws<ObjectDisposedException>(() => game.Start(), "restart after incomplete disposal");
            Throws<ObjectDisposedException>(() => game.Load(saved), "load after incomplete disposal");
            room.Player.LocalTransform = Transform2D.Identity; // Repair through a retained World reference, not a reopened mission.
            game.Dispose(); game.Dispose();
            Check(room.World.EntityCount == 0 && !room.ActiveScene.IsLoaded && releases == 2,
                "closed mission permits teardown retry after numeric repair, releasing retained attachments once");
        }

        foreach (bool disposeWithPending in new[] { false, true })
        {
            using var game = new MissionGame(path, catalog);
            game.Start(); string saved = game.Save();
            var old = game.Room;
            var (parent, _) = InvalidRetirement(old);
            int pendingReleases = 0, currentReleases = 0, prepares = 0;
            old.World.AttachBehavior(parent, new IdleBehavior(), scope => scope.OnDetach(() => pendingReleases++));
            old.World.AttachBehavior(old.Player, new IdleBehavior(), scope => scope.OnDetach(() => pendingReleases++));
            Throws<AggregateException>(() => game.Start(), "incomplete old retirement after committed replacement");
            var current = game.Room;
            Check(!ReferenceEquals(old, current) && current.World.EntityCount == 6 && old.World.EntityCount == 2,
                "preflight-blocked old room remains owned alongside the committed candidate");
            current.World.AttachBehavior(current.Player, new IdleBehavior(), scope => scope.OnDetach(() => currentReleases++));
            if (disposeWithPending)
            {
                Throws<AggregateException>(() => game.Dispose(), "pending old retirement during disposal");
                Check(current.World.EntityCount == 0 && currentReleases == 1 && parent.IsAlive && pendingReleases == 1,
                    "disposal attempts current room even when pending retirement is still blocked");
                Throws<ObjectDisposedException>(() => game.Load(saved), "load with closed pending retirement");
                parent.LocalTransform = Transform2D.Identity;
                game.Dispose(); game.Dispose();
            }
            else
            {
                Throws<AggregateException>(() => game.Load(saved, _ => prepares++), "pending retirement before another load");
                Check(ReferenceEquals(current, game.Room) && current.World.EntityCount == 6 && prepares == 0 && parent.IsAlive,
                    "pending cleanup is retried before creating or preparing another candidate");
                parent.LocalTransform = Transform2D.Identity;
                game.Start(_ => prepares++);
                Check(prepares == 1 && current.World.EntityCount == 0 && currentReleases == 1,
                    "successful pending cleanup permits the next replacement");
            }
            Check(old.World.EntityCount == 0 && !old.ActiveScene.IsLoaded && pendingReleases == 2,
                "pending room ownership persists until all retained entities and scene are retired");
        }

        using (var game = new MissionGame(path, catalog))
        {
            game.Start(); string saved = game.Save();
            var current = game.Room;
            var (parent, _) = InvalidRetirement(current);
            RoomGame? candidate = null;
            int nestedPrepares = 0, releases = 0;
            Throws<InvalidOperationException>(() => game.Start(prepared =>
            {
                candidate = prepared;
                prepared.World.AttachBehavior(prepared.Player, new IdleBehavior(), scope => scope.OnDetach(() => releases++));
                Throws<InvalidOperationException>(() => game.Load(saved, _ => nestedPrepares++), "load from preparation callback");
                Throws<InvalidOperationException>(() => game.LoadFile(Path.Combine(path, "missing.json")), "file load from preparation callback");
                game.Start(_ => nestedPrepares++); // Propagate this rejection to exercise candidate rollback too.
            }), "restart from preparation callback");
            Check(ReferenceEquals(game.Room, current) && current.World.EntityCount == 7 && parent.IsAlive && current.Player.IsAlive
                && candidate is not null && candidate.World.EntityCount == 0 && releases == 1 && nestedPrepares == 0,
                "nested preparation cannot create a pending owner or overwrite ownership during candidate rollback");
            current.Player.LocalTransform = Transform2D.Identity;
            game.Load(saved);
            Check(current.World.EntityCount == 0 && game.Room.World.EntityCount == 6 && releases == 1,
                "preparation guard clears after failure and permits a later replacement");
        }

        foreach (bool loading in new[] { false, true })
        {
            using var game = new MissionGame(path, catalog);
            game.Start(); string saved = game.Save();
            var current = game.Room;
            RoomGame? candidate = null;
            int releases = 0;
            void Prepare(RoomGame prepared)
            {
                candidate = prepared;
                prepared.World.AttachBehavior(prepared.Player, new IdleBehavior(), scope => scope.OnDetach(() => releases++));
                game.Dispose();
            }
            Throws<ObjectDisposedException>(() =>
            {
                if (loading) game.Load(saved, Prepare);
                else game.Start(Prepare);
            }, "disposal during candidate preparation");
            Check(ReferenceEquals(game.Room, current) && current.World.EntityCount == 0 && candidate is not null
                && candidate.World.EntityCount == 0 && !candidate.ActiveScene.IsLoaded && releases == 1,
                "preparation cannot resurrect a disposed mission and rejected candidate cleanup runs once");
            Throws<ObjectDisposedException>(() => game.Load(saved), "load after disposal during preparation");
            game.Dispose();
            Check(releases == 1, "repeated disposal does not retain or repeat rejected candidate cleanup");
        }

        foreach (bool loading in new[] { false, true })
        {
            using var game = new MissionGame(path, catalog);
            game.Start(); string saved = game.Save();
            var old = game.Room;
            int releases = 0;
            old.World.AttachBehavior(old.Item, new IdleBehavior(), scope => scope.OnDetach(() => throw new InvalidOperationException("old run cleanup")));
            old.World.AttachBehavior(old.Player, new IdleBehavior(), scope => scope.OnDetach(() => releases++));
            RoomGame? replacement = null;
            int nestedPrepares = 0;
            old.World.AttachBehavior(old.World.Entities.Single(entity => entity.Name == "Status"), new IdleBehavior(), scope => scope.OnDetach(() =>
            {
                Check(old.World.EntityCount == 0, "last persistent callback runs after all old entities are destroyed");
                Throws<InvalidOperationException>(() => game.Start(_ => nestedPrepares++), "restart from old retirement callback");
                Throws<InvalidOperationException>(() => game.Load(saved, _ => nestedPrepares++), "load from old retirement callback");
                Throws<InvalidOperationException>(() => game.LoadFile(Path.Combine(path, "missing.json")), "file load from old retirement callback");
                game.Dispose();
                Check(ReferenceEquals(game.Room, replacement) && game.Room.World.EntityCount == 6 && nestedPrepares == 0,
                    "nested replacement and disposal cannot change the committed candidate or pending ownership");
            }));
            Throws<AggregateException>(() =>
            {
                if (loading) game.Load(saved, candidate => replacement = candidate);
                else game.Start(candidate => replacement = candidate);
            }, "old run cleanup after replacement");
            Check(ReferenceEquals(game.Room, replacement) && game.Room.World.EntityCount == 6
                && game.Screen == (loading ? MissionScreen.Paused : MissionScreen.Playing)
                && old.World.EntityCount == 0 && !old.ActiveScene.IsLoaded && releases == 1,
                "old cleanup failure does not undo committed replacement or skip persistent cleanup");
            Check(game.Save().Length > 0, "nested disposal during retirement leaves the live mission usable");
        }
        return count;
    }

    private sealed class IdleBehavior : IBehavior { public void Update(Entity entity, float dt) { } }
}
