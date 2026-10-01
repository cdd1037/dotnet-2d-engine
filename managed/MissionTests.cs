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
        var catalog = new AssetCatalog();
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
            using (var isolated = new MissionGame(path, new AssetCatalog(assetDirectory)))
            {
                var usable = isolated.Room;
                File.Delete(Path.Combine(assetDirectory, "room-b.bmp"));
                Reject(() => isolated.Start(), "missing destination asset");
                Check(ReferenceEquals(usable, isolated.Room) && usable.World.EntityCount == 6, "missing resource preserves usable world");
                File.WriteAllText(Path.Combine(assetDirectory, "room-b.bmp"), "bad BMP");
                Reject(() => isolated.Start(), "invalid destination asset");
                Check(ReferenceEquals(usable, isolated.Room), "invalid resource preserves usable world");
            }
            game.Dispose(); game.Dispose();
            Check(game.Room.World.EntityCount == 0 && subscribers == 0, "final idempotent disposal");
            Console.WriteLine($"MISSION SELF-TEST PASS assertions={count}");
            return count;
        }
        finally { Directory.Delete(directory, true); }
    }
    private sealed class IdleBehavior : IBehavior { public void Update(Entity entity, float dt) { } }
}
