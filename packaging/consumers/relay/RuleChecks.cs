using GameAuthoringLab;
using System.Text.Json.Nodes;

namespace Relay;

// Application-owned regressions: the complete mission/retirement suite plus
// room/save-schema checks. Fixtures use only this app and the public package;
// no native context, repository test helper, or engine source is required.
internal static class RuleChecks
{
    public static int Run(string assetRoot)
    {
        int count = MissionRules(assetRoot) + RoomRuleChecks.Run(assetRoot) + PersistenceRuleChecks.Run();
        count += AppValidationChecks(assetRoot);
        Console.WriteLine($"RELAY RULE CHECK PASS assertions={count} native=false");
        return count;
    }

    private static int AppValidationChecks(string assetRoot)
    {
        int count = 0;
        void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException("RELAY APP: " + message);
            count++;
        }
        void RejectText(string? text, int bytes, int scalars)
        {
            try { RelayValidation.ValidateText(text!, bytes, scalars, "$.title"); }
            catch (UiAuthoringException error)
            {
                Check(error.Code == "UI_MODEL" && error.Field == "$.title", "bounded text reports original diagnostic contract");
                return;
            }
            throw new InvalidOperationException("RELAY APP accepted invalid bounded text");
        }
        RelayValidation.ValidateText(new string('A', 64), 96, 64, "$.title");
        RelayValidation.ValidateText(string.Concat(Enumerable.Repeat("\U0001F680", 24)), 96, 64, "$.title");
        count += 2;
        RejectText(null, 96, 64);
        RejectText(new string('A', 65), 96, 64);
        RejectText(new string('A', 97), 96, 64);
        RejectText(string.Concat(Enumerable.Repeat("\U0001F680", 25)), 96, 64);
        RejectText("\ud800", 96, 64);
        RejectText("\udc00", 96, 64);
        RejectText("line\nfeed", 96, 64);
        RejectText("embedded\0null", 96, 64);

        var controls = new RelayInput();
        foreach (uint action in new[] { RelayInput.Left, RelayInput.Right, RelayInput.Up, RelayInput.Down,
            RelayInput.Space, RelayInput.Escape, RelayInput.Interact, RelayInput.Drop,
            RelayInput.Transition, RelayInput.Save, RelayInput.Load })
        {
            var state = controls.ToRules(controls.TestState(action, action));
            Check(state.Down == action && state.Pressed == action, "typed action translates into the intended application rule");
        }
        Check(controls.ToRules(default) == (0u, 0u), "default typed state is neutral");

        var catalog = RelayAssets.Catalog(assetRoot);
        using var game = new MissionGame(Path.Combine(assetRoot, "relay.mission.json"), catalog);
        game.Start(); game.Advance(0, 0);
        game.Room.Player.LocalTransform = new Transform2D(230, 280);
        game.Advance(0, RelayInput.Interact, RoomGame.FixedDelta / 2);
        Check(game.Room.Held is null, "short input edge is buffered until a fixed step");
        game.Advance(0, 0, RoomGame.FixedDelta / 2);
        Check(game.Room.Held == game.Room.Item, "pressed-only interaction survives to the fixed step");
        game.Pause(); game.Resume();
        game.Advance(0, RelayInput.Drop, RoomGame.FixedDelta);
        Check(game.WaitingForNeutral && game.Room.Held == game.Room.Item, "neutral boundary blocks pressed-only actions");
        game.Advance(0, 0, 0);
        game.Room.Player.LocalTransform = new Transform2D(830, 280);
        game.Advance(0, RelayInput.Transition, RoomGame.FixedDelta);
        game.Room.Player.LocalTransform = new Transform2D(game.Definition.DeliveryX, game.Definition.DeliveryY);
        var saved = JsonNode.Parse(game.Save())!.AsObject(); saved["remainingTicks"] = 1;
        game.Load(saved.ToJsonString()); game.Resume(); game.Advance(0, 0);
        game.Advance(0, RelayInput.Interact, RoomGame.FixedDelta / 2);
        Check(game.Screen == MissionScreen.Playing && game.RemainingTicks == 1, "delivery waits for a fixed tick");
        game.Advance(0, 0, RoomGame.FixedDelta / 2);
        Check(game.Screen == MissionScreen.Won && game.RemainingTicks == 0, "buffered delivery wins the final-tick tie");
        Console.WriteLine($"RELAY APP BOUNDARIES PASS assertions={count}");
        return count;
    }

    private static int MissionRules(string assetRoot)
    {
        int count = 0;
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException("MISSION: " + message); count++; }
        void Reject(Action action, string message) { try { action(); } catch (Exception e) when (e is SceneFormatException or UiAuthoringException or IOException or InvalidOperationException) { count++; return; } throw new Exception("MISSION accepted " + message); }
        string directory = Path.Combine(Path.GetTempPath(), "gal-mission-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var catalog = RelayAssets.Catalog(assetRoot);
        string path = Path.Combine(directory, "relay.mission.json");
        string source = File.ReadAllText(Path.Combine(catalog.Root, "relay.mission.json"));
        File.WriteAllText(path, source);
        try
        {
            using var game = new MissionGame(path, catalog);
            Check(game.Screen == MissionScreen.Title && game.Room.World.EntityCount == 6, "title has one frozen room");
            var titlePosition = game.Room.Player.LocalTransform;
            game.Advance(RelayInput.Right, 10);
            Check(game.Room.Player.LocalTransform == titlePosition, "title does not simulate");
            var titleWorld = game.Room.World;
            game.Start();
            Check(titleWorld.EntityCount == 0 && game.Screen == MissionScreen.Playing, "start retires title world");
            var start = game.Room.Player.LocalTransform;
            game.Advance(RelayInput.Right | RelayInput.Interact, 10);
            Check(game.Room.Player.LocalTransform == start && game.RemainingTicks == 5400, "start holds all input until neutral");
            game.Advance(0, 10); game.Advance(RelayInput.Right, RoomGame.FixedDelta);
            Check(game.Room.Player.LocalTransform.X == start.X + 3 && game.RemainingTicks == 5399, "one fresh tick after start");
            game.Room.Player.LocalTransform = new Transform2D(230, 280);
            game.Room.Advance(RelayInput.Interact, RoomGame.FixedDelta / 4);
            int ticks = game.RemainingTicks;
            game.Pause();
            for (int i = 0; i < 20; i++) game.Advance(RelayInput.Right | RelayInput.Interact, 1);
            Check(game.RemainingTicks == ticks && game.Room.Held is null, "pause freezes timer and pending pickup");
            game.Resume(); game.Advance(RelayInput.Interact, 1); game.Advance(0, 1); game.Advance(0, RoomGame.FixedDelta);
            Check(game.Room.Held is null && game.RemainingTicks == ticks - 1, "resume clears pending edge and elapsed backlog");
            game.Advance(RelayInput.Interact, RoomGame.FixedDelta);
            Check(game.Room.Held == game.Room.Item, "normal pickup");
            game.Room.Player.LocalTransform = new Transform2D(830, 280);
            Scene firstRoom = game.Room.ActiveScene;
            game.Advance(RelayInput.Transition, RoomGame.FixedDelta);
            Check(game.Room.RoomIndex == 1 && !firstRoom.IsLoaded && game.Room.Held is not null, "cross room carrying cell");
            string checkpoint = game.Save(); var oldWorld = game.Room.World; var playerId = game.Room.Player.PersistentId;
            int savedTicks = game.RemainingTicks;
            game.Load(checkpoint);
            Check(oldWorld.EntityCount == 0 && game.Room.Player.PersistentId == playerId && game.Screen == MissionScreen.Paused && game.RemainingTicks == savedTicks, "load swaps fresh world preserving progress and enters pause");
            game.Resume(); game.Advance(0, 0);
            game.Room.Player.LocalTransform = new Transform2D(game.Definition.DeliveryX, game.Definition.DeliveryY);
            game.Advance(0, RoomGame.FixedDelta);
            Check(game.Screen == MissionScreen.Playing, "arrival alone does not deliver");
            game.Advance(RelayInput.Interact, RoomGame.FixedDelta);
            Check(game.Screen == MissionScreen.Won, "E delivers in target zone");
            int terminalTicks = game.RemainingTicks; game.Advance(RelayInput.Right, 1);
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
            game.Advance(RelayInput.Right, .25f);
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
            foreach (string asset in Directory.GetFiles(catalog.Root, "*.png")) File.Copy(asset, Path.Combine(assetDirectory, Path.GetFileName(asset)));
            using (var isolated = new MissionGame(path, RelayAssets.Catalog(assetDirectory)))
            {
                var usable = isolated.Room;
                File.Delete(Path.Combine(assetDirectory, "room-b.png"));
                Reject(() => isolated.Start(), "missing destination asset");
                Check(ReferenceEquals(usable, isolated.Room) && usable.World.EntityCount == 6, "missing resource preserves usable world");
                File.WriteAllText(Path.Combine(assetDirectory, "room-b.png"), "bad PNG");
                Reject(() => isolated.Start(), "invalid destination asset");
                Check(ReferenceEquals(usable, isolated.Room), "invalid resource preserves usable world");
            }
            count += RetirementFailures(path, catalog);
            game.Dispose(); game.Dispose();
            Check(game.Room.World.EntityCount == 0 && subscribers == 0, "final idempotent disposal");
            Console.WriteLine($"RELAY MISSION RULES PASS assertions={count}");
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

internal static class RoomRuleChecks
{
    private static int _count;
    public static int Run(string assetRoot)
    {
        _count=0;var catalog=RelayAssets.Catalog(assetRoot);
        foreach(string key in new[]{"room-a","room-b","player","cell","status-empty","status-held","status-restored"})Check(File.Exists(catalog.PathFor(key)),"registered app texture exists");
        var game=new RoomGame();RoomGame restored=Exercise(game,catalog);
        string saved=restored.Save();
        Expect(()=>RoomGame.Load("{broken",catalog));
        Expect(()=>RoomGame.Load(saved.Replace("room-b","missing-art",StringComparison.Ordinal),catalog));
        Check(restored.Player.IsAlive&&restored.RoomIndex==1,"failed load does not mutate current game");
        Expect(()=>RoomGame.Load(Change(saved,"Player",e=>{e["transform"]!["x"]=420;e["transform"]!["y"]=200;}),catalog));
        Expect(()=>RoomGame.Load(Change(saved,"Player",e=>e["transform"]!["rotation"]=.2),catalog));
        Expect(()=>RoomGame.Load(Change(saved,"Player",e=>e["sprite"]!["width"]=100),catalog));
        Expect(()=>RoomGame.Load(Change(saved,"Status",e=>e["sceneId"]=restored.ActiveScene.PersistentId.ToString()),catalog));
        Expect(()=>RoomGame.Load(Change(saved,"Status",e=>{e["sprite"]=null;e["transform"]!["scaleX"]=3e38;}),catalog));
        Expect(()=>RoomGame.Load(Change(saved,"RoomBackdrop",e=>e["transform"]!["rotation"]=.1),catalog));
        var fixedGame=new RoomGame();float x=fixedGame.Player.LocalTransform.X;
        fixedGame.Advance(RelayInput.Right,1f);Check(fixedGame.LastSteps==8,"fixed-step backlog capped");Check(MathF.Abs(fixedGame.Player.LocalTransform.X-x-24)<.01f,"capped fixed-step movement");
        var a=new RoomGame();var b=new RoomGame();a.Advance(RelayInput.Right,1f/30);b.Advance(RelayInput.Right,1f/60);b.Advance(RelayInput.Right,1f/60);Check(a.Player.LocalTransform==b.Player.LocalTransform,"fixed-step partition equivalence");
        Expect(()=>fixedGame.Advance(0,float.NaN));
        Console.WriteLine("PASS two-room movement/AABB, pickup, transition, drop, persistence, missing assets and bounded fixed-step");return _count;
    }
    public static RoomGame Exercise(RoomGame game,AssetCatalog catalog,Action<RoomGame>? frame=null)
    {
        Guid player=game.Player.PersistentId,item=game.Item.PersistentId;EntityId oldPlayer=game.Player.Id;Scene oldScene=game.ActiveScene;
        Entity floor=game.World.Entities.Single(e=>e.Name=="RoomBackdrop");frame?.Invoke(game);
        for(int i=0;i<23;i++)Tick(game,RelayInput.Right,frame);
        Transform2D itemBefore=game.Item.WorldTransform;Check(game.PickUp(),"pickup within range");Near(game.Item.WorldTransform,itemBefore);Check(game.Item.PersistentId==item,"pickup identity");frame?.Invoke(game);
        string heldSave=game.Save();RoomGame heldReload=RoomGame.Load(heldSave,catalog);Check(heldReload.Held==heldReload.Item&&heldReload.Item.TransformParent==heldReload.Player,"held relationship survives save");
        for(int i=0;i<30;i++)Tick(game,RelayInput.Up,frame);for(int i=0;i<80;i++)Tick(game,RelayInput.Right,frame);
        Check(game.CollisionCount>0&&game.Player.LocalTransform.X<=364.01f,"AABB blocks workbench");
        for(int i=0;i<37;i++)Tick(game,RelayInput.Down,frame);for(int i=0;i<151;i++)Tick(game,RelayInput.Right,frame);
        Check(game.UseDoor(),"door transition in range");Check(game.RoomIndex==1&&!oldScene.IsLoaded&&!floor.IsAlive,"old room fixtures unload");
        Check(game.Player.PersistentId==player&&game.Item.PersistentId==item&&game.Item.IsAlive&&game.Held==game.Item,"player and carried item survive transition");frame?.Invoke(game);
        for(int i=0;i<44;i++)Tick(game,RelayInput.Right,frame);
        Transform2D dropped=game.Item.WorldTransform;Check(game.Drop(),"drop carried item");Near(game.Item.WorldTransform,dropped);Check(game.Item.Scene==game.ActiveScene&&game.Item.TransformParent is null&&game.Item.LifetimeOwner is null,"drop has room lifetime");frame?.Invoke(game);
        string json=game.Save();RoomGame restored=RoomGame.Load(json,catalog);
        Check(restored.Player.PersistentId==player&&restored.Item.PersistentId==item&&restored.Player.Id!=oldPlayer,"persistent IDs survive restart, runtime IDs change");
        Near(restored.Item.WorldTransform,dropped);Check(restored.Held is null&&restored.RoomIndex==1&&restored.TransitionCount==1&&restored.PickupCount==1,"game state restored");frame?.Invoke(restored);
        // A dropped cell remains the same logical entity while its room is inactive.
        RoomGame revisit=RoomGame.Load(json,catalog);for(int i=0;i<45;i++)revisit.Step(RelayInput.Left);
        Check(revisit.UseDoor()&&revisit.Item.Sprite is null,"inactive room item hidden without destruction");
        Check(revisit.Item.PersistentId==item&&revisit.Item.IsAlive,"inactive room cell identity retained");
        string hiddenSave=revisit.Save();
        Expect(()=>RoomGame.Load(Change(hiddenSave,"PowerCell",e=>e["transform"]!["scaleX"]=1e38),catalog));
        Expect(()=>RoomGame.Load(Change(hiddenSave,"PowerCell",e=>{e["transform"]!["scaleX"]=1.3e37;e["transform"]!["rotation"]=Math.PI/4;}),catalog));
        for(int i=0;i<3;i++)revisit.Step(RelayInput.Right);
        Check(revisit.UseDoor()&&revisit.Item.Sprite?.AssetKey=="cell","returning restores dropped cell");Near(revisit.Item.WorldTransform,dropped);
        return restored;
    }
    private static string Change(string json,string name,Action<System.Text.Json.Nodes.JsonObject> edit)
    {
        var document=System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
        var entity=document["entities"]!.AsArray().Single(e=>e!["name"]!.GetValue<string>()==name)!.AsObject();edit(entity);return document.ToJsonString();
    }
    private static void Tick(RoomGame g,uint keys,Action<RoomGame>? frame){g.Step(keys);frame?.Invoke(g);}
    private static void Near(Transform2D a,Transform2D b)
    {
        a.GetBasis(out float a1,out float a2,out float a3,out float a4);b.GetBasis(out float b1,out float b2,out float b3,out float b4);
        Check(MathF.Abs(a.X-b.X)<.001f&&MathF.Abs(a.Y-b.Y)<.001f&&MathF.Abs(a1-b1)<.001f&&MathF.Abs(a2-b2)<.001f&&MathF.Abs(a3-b3)<.001f&&MathF.Abs(a4-b4)<.001f,"world affine preserved");
    }
    private static void Check(bool value,string message){_count++;if(!value)throw new InvalidOperationException("Room test: "+message);}
    private static void Expect(Action action){try{action();}catch(Exception e) when(e is SceneFormatException or FileNotFoundException or ArgumentOutOfRangeException){_count++;return;}throw new InvalidOperationException("Expected rejected room data");}
}

internal static class PersistenceRuleChecks
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
