using System.Buffers.Binary;
using System.Numerics;
using GameAuthoringLab;

// An opt-in authored integration path in the existing feature consumer, not a game framework.
internal static class AuthorSmoke
{
    internal static int Run(EngineHost engine, AssetRoot assets, PhysicsWorld physics,
        PhysicsBody body, TileMapInstance map, TileMapCollision collision, PhysicsScale scale,
        World world, Entity actor, TextureBank textures, SpriteBatch batch)
    {
        int assertions = 0;
        void Check(bool condition, string why)
        {
            if (!condition) throw new InvalidOperationException("AUTHOR SMOKE: " + why);
            assertions++;
        }
        string captures = Path.GetFullPath(Environment.GetEnvironmentVariable("GAL_AUTHOR_CAPTURE_DIR") ?? "author-smoke-captures");
        Directory.CreateDirectory(captures);
        using var ui = new UiModelSession<string>(engine, new UiRecord<string>().Text("caption", state => state));
        ui.StageAsset(assets, "author.rml", "Preparing marker-driven floor");
        engine.Draw(new Camera { Zoom = 1 }, ReadOnlySpan<SpriteCommand>.Empty);
        Check(ui.Status.Loaded && !ui.Status.Pending, "authored image/SVG overlay publishes");
        var viewport = engine.PollInputFrame().Viewport;
        Check(viewport.IsValid && viewport.PixelWidth == 960 && viewport.PixelHeight == 540,
            "software smoke uses the declared 960x540 framebuffer");
        using var player = new FramePlayer(new FrameClip(["red", "blue", "red"], .25,
            [new(0, 10), new(1, 20), new(2, 30)]), eventCapacity: 2);
        var camera = CameraFollow.Snap(new Camera { Zoom = 2 },
            new(scale.ToPixels(body.State.X), scale.ToPixels(body.State.Y)), viewport);
        Span<ulong> hits = stackalloc ulong[4];
        float initialY = body.State.Y;
        for (int phase = 0; phase < 3; phase++)
        {
            player.Advance(TimingStep.FromReal(phase == 0 ? .01 : .25));
            Check(player.Events.Length == 1 && player.Events[0].EventId == (phase + 1) * 10,
                "one polled frame marker selects each author phase");
            bool opened = player.Events[0].EventId == 20;
            map.SetCells([new(0, 2, 4, opened ? 0 : 1)]);
            int count = physics.QueryCircle(2.5f, 4.2f, .1f, hits, mask: 1);
            Check(opened ? count == 0 : count == 1 && collision.TryGetRectangle(hits[0], out _),
                "marker-driven map edit and exact collision query agree immediately");
            if (phase == 2) { body.Teleport(2.5f, initialY); body.SetVelocity(0, 0); }
            for (int step = 0; step < (opened ? 24 : 3); step++)
                Check(physics.Step().Dropped == 0, "smoke physics event queue retains all events");
            Check(opened ? body.State.Y > initialY + .5f : Math.Abs(body.State.Y - initialY) < .04f,
                "capsule falls only when the marker opens its floor");
            var target = new Vector2(scale.ToPixels(body.State.X), scale.ToPixels(body.State.Y));
            actor.LocalTransform = new(target.X - 8, target.Y - 16);
            actor.Sprite = new(16, 32, AssetKey: player.AssetKey, Layer: 2);
            textures.Sync(world);
            var previousCamera = camera;
            camera = CameraFollow.Update(camera, target, viewport, TimingStep.FromReal(.25),
                new(HalfLifeSeconds: .05));
            Check(phase == 0 || (opened ? camera.Y > previousCamera.Y : camera.Y < previousCamera.Y),
                "camera follows the simulated actor after removal and restore");
            var paused = CameraFollow.Update(camera, target + new Vector2(0, 10), viewport,
                TimingStep.FromReal(.25, paused: true), new(HalfLifeSeconds: .05));
            player.Advance(TimingStep.FromReal(.25, paused: true));
            Check(paused.Y == camera.Y && player.Events.IsEmpty,
                "game pause leaves camera and frame-entry side effects unchanged");
            string label = phase switch { 0 => "10 / Floor closed", 1 => "20 / Gap opened", _ => "30 / Floor restored" };
            Check(ui.Apply(label) && ui.Revision == (uint)(phase + 2), "UI model advances with the same marker transaction");
            world.ExtractSprites(batch);
            map.AppendSprites(batch, TileView.FromCamera(camera, viewport));
            Check(batch.Count == (opened ? 7 : 8), "visible edited map and actor share one coherent batch");
            string path = Path.Combine(captures, $"{phase}-{(opened ? "open" : "closed")}.bmp");
            ui.Capture(path);
            engine.Draw(camera, batch);
            var bitmap = File.ReadAllBytes(path);
            Check(Near(Pixel(bitmap, 48, 80), (255, 0, 0), 4) && Near(Pixel(bitmap, 80, 80), (0, 0, 255), 4),
                "authored raster image retains both caller-atlas colors");
            Check(Pixel(bitmap, 144, 80) == (0, 255, 0), "authored SVG badge renders actual green pixels");
            (int X, int Y) Screen(float x, float y) =>
                ((int)MathF.Round((x - camera.X) * camera.Zoom), (int)MathF.Round((y - camera.Y) * camera.Zoom));
            var gap = Screen(68, 144);
            var neighbor = Screen(112, 144);
            var center = Screen(target.X, target.Y);
            Check(Pixel(bitmap, gap.X, gap.Y) == (opened ? Pixel(bitmap, 900, 500) : (255, 0, 0)),
                "framebuffer floor pixel matches committed collision/edit state");
            Check(Pixel(bitmap, neighbor.X, neighbor.Y) == (255, 0, 0), "adjacent retained floor stays visible");
            Check(Pixel(bitmap, center.X, center.Y) == (opened ? (0, 0, 255) : (255, 0, 0)),
                "camera and animated actor share world-to-framebuffer coordinates");
            Check(engine.Textures.Count == 1, "document images/SVG do not acquire world atlas leases");
        }
        Console.WriteLine($"AUTHOR SMOKE PASS assertions={assertions} phases=closed/open/restored camera=follow markers=polled tile-collision=coherent raster=verified svg=verified captures={captures}");
        return assertions;
    }

    private static bool Near((int R, int G, int B) actual, (int R, int G, int B) expected, int tolerance) =>
        Math.Abs(actual.R - expected.R) <= tolerance && Math.Abs(actual.G - expected.G) <= tolerance &&
        Math.Abs(actual.B - expected.B) <= tolerance;

    // SDL's readback BMP is BGR(A), bottom-up or top-down; keep this fixture independent of test-host helpers.
    private static (int R, int G, int B) Pixel(byte[] bytes, int x, int y)
    {
        if (bytes.Length < 54 || bytes[0] != 'B' || bytes[1] != 'M') throw new InvalidOperationException("Expected BMP capture");
        int offset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(10));
        int width = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(18));
        int signedHeight = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(22));
        int height = Math.Abs(signedHeight), bits = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(28));
        uint compression = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(30));
        if (width <= 0 || height <= 0 || bits is not (24 or 32) || compression is not (0 or 3) ||
            x < 0 || y < 0 || x >= width || y >= height) throw new InvalidOperationException("Unsupported BMP coordinate/format");
        int stride = checked(((width * bits + 31) / 32) * 4);
        int index = checked(offset + (signedHeight > 0 ? height - 1 - y : y) * stride + x * (bits / 8));
        if (index < 54 || index + 2 >= bytes.Length) throw new InvalidOperationException("Truncated BMP capture");
        return (bytes[index + 2], bytes[index + 1], bytes[index]);
    }
}
