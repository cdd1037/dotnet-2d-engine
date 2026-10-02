using System.Numerics;
namespace GameAuthoringLab;

internal static class DrawingCommandTests
{
    public static int Run(bool graphics = false)
    {
        int checks = 0;
        void Check(bool value, string label) { if (!value) throw new Exception("DRAW COMMAND: " + label); checks++; }
        void Reject<T>(Action action, string label) where T : Exception
        { try { action(); } catch (T) { checks++; return; } throw new Exception("DRAW COMMAND accepted: " + label); }
        var assets = new AssetRoot(); var camera = new Camera { Zoom = 1 };
        using var engine = EngineHost.Create(!graphics, 16);
        using var lease = engine.Textures.Acquire(assets, "regions.bmp");
        using var retained = engine.Textures.Acquire(assets, "regions.bmp");
        using var material = engine.Materials.Acquire(assets, "materials/tint.material.json");
        using var target = engine.RenderTargets.Create(32, 32);
        var solid = new SpriteCommand(new(0, 0), new(16, 16), Vector4.One);
        var textured = solid with { Texture = lease.Texture, Region = new(0, 0, 1, 1), FlipX = true };
        var custom = textured with { Material = material.Material, Parameters = new(Vector4.One) };
        SpriteCommand[] draws = [solid, textured, custom];
        uint frames = engine.GetStats().Frames;
        engine.Draw(camera, draws); Check(engine.GetStats().Frames == frames + 1, "mixed safe commands render in one frame");
        engine.Draw(camera, ReadOnlySpan<SpriteCommand>.Empty); Check(engine.GetStats().Frames == frames + 2, "empty command span remains a valid frame");
        engine.Draw(camera, new[] { solid with { Size = Vector2.Zero }, solid with { Size = new(0, 8) } });
        Check(engine.GetStats().Frames == frames + 3, "zero extents preserve the existing sprite contract");
        Reject<ArgumentException>(() => engine.Draw(camera, new SpriteCommand[17]), "bounded command conversion");
        Reject<ArgumentException>(() => engine.Draw(camera, new[] { solid, solid with { Region = new(0, 0, 1, 1) } }), "solid source region rejected before frame begins");
        Reject<ArgumentOutOfRangeException>(() => engine.Draw(camera, new[] { textured with { Region = new(-1, 0, 1, 1) } }), "headless and graphics texture region bounds");
        Reject<ArgumentOutOfRangeException>(() => engine.Draw(camera, new[] { solid with { Tint = new(2, 1, 1, 1) } }), "managed tint validation");
        Reject<ArgumentException>(() => engine.Draw(camera, new[] { solid with { Parameters = new(Vector4.One) } }), "default material rejects constants");
        Reject<ArgumentException>(() => engine.RenderFrame(new[] { FramePass.ToTarget(default, camera, 0, 0), FramePass.Window(camera, 0, 0) }, ReadOnlySpan<SpriteCommand>.Empty), "default target never aliases window");
        Reject<ArgumentException>(() => engine.RenderFrame(new FramePass[1], ReadOnlySpan<SpriteCommand>.Empty), "default pass requires explicit factory");
        SpriteCommand[] post = [solid, solid with { Texture = target.Texture }];
        FramePass[] passes = [FramePass.ToTarget(target.Target, camera, 0, 1), FramePass.Window(camera, 1, 1)];
        engine.RenderFrame(passes, post); Check(true, "explicit sample view allows offscreen composition");
        uint beforeRejected = engine.GetStats().Frames;
        Reject<InvalidOperationException>(() => engine.RenderFrame(passes, new[] { post[1], post[1] }), "same target read/write feedback rejected transactionally");
        Reject<InvalidOperationException>(() => engine.RenderFrame(new[] { FramePass.ToTarget(target.Target, camera, 0, 0), FramePass.Window(camera, 1, 1) }, post), "draw range gap rejected transactionally");
        Check(engine.GetStats().Frames == beforeRejected, "rejected plan has no completed partial frame");
        engine.RenderFrame(new[] { FramePass.ToTarget(target.Target, camera, 0, 0), FramePass.Window(camera, 0, 0) }, ReadOnlySpan<SpriteCommand>.Empty);
        var world = new World(); var entity = world.Create("safe-batch"); entity.Sprite = new Sprite2D(8, 8);
        var batch = new SpriteBatch(2); world.ExtractSprites(batch);
        SpriteCommand[] overlay = [solid, solid];
        uint beforeCalls = engine.GetStats().DrawCalls;
        engine.DrawWithOverlay(camera, batch, overlay);
        Check(engine.GetStats().DrawCalls - beforeCalls == (graphics ? 1 : 0), "safe scene and overlay preserve effective contiguous batching");
        engine.Draw(camera, batch);
        for (int i = 0; i < 128; i++) { engine.Draw(camera, draws); engine.RenderFrame(passes, post); engine.DrawWithOverlay(camera, batch, overlay); }
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) { engine.Draw(camera, draws); engine.RenderFrame(passes, post); engine.DrawWithOverlay(camera, batch, overlay); }
        Check(GC.GetAllocatedBytesForCurrentThread() == bytes, "warmed conversion, passes and overlays allocate zero bytes");
        var staleTexture = lease.Texture; lease.Dispose();
        Check(engine.Textures.Count == 1, "independent retained lease still owns resource");
        Reject<ObjectDisposedException>(() => engine.Draw(camera, new[] { solid with { Texture = staleTexture } }), "borrowed copy detects exact disposed lease despite another retention");
        var staleMaterial = material.Material; material.Dispose();
        Reject<ObjectDisposedException>(() => engine.Draw(camera, new[] { solid with { Material = staleMaterial } }), "copied material detects disposed lease");
        var staleTarget = target.Target; var staleSample = target.Texture; target.Dispose();
        Reject<ObjectDisposedException>(() => engine.RenderFrame(new[] { FramePass.ToTarget(staleTarget, camera, 0, 0), FramePass.Window(camera, 0, 0) }, ReadOnlySpan<SpriteCommand>.Empty), "copied target detects disposal");
        Reject<ObjectDisposedException>(() => engine.Draw(camera, new[] { solid with { Texture = staleSample } }), "copied sample detects target disposal");
        var engineFirst = retained.Texture;
        using var contextMaterial = engine.Materials.Acquire(assets, "materials/tint.material.json");
        using var contextTarget = engine.RenderTargets.Create(8, 8);
        var foreignMaterial = contextMaterial.Material; var foreignTarget = contextTarget.Target; var foreignSample = contextTarget.Texture;
        engine.Dispose();
        using var next = EngineHost.Create(true, 16);
        Reject<ObjectDisposedException>(() => next.Draw(camera, new[] { solid with { Texture = engineFirst } }), "engine-first cleanup invalidates borrowed copy");
        Reject<ObjectDisposedException>(() => next.Draw(camera, new[] { solid with { Material = foreignMaterial } }), "prior-context material rejects replacement engine");
        Reject<ObjectDisposedException>(() => next.Draw(camera, new[] { solid with { Texture = foreignSample } }), "prior-context sample rejects replacement engine");
        Reject<ObjectDisposedException>(() => next.RenderFrame(new[] { FramePass.ToTarget(foreignTarget, camera, 0, 0), FramePass.Window(camera, 0, 0) }, ReadOnlySpan<SpriteCommand>.Empty), "prior-context attachment rejects replacement engine");
        Console.WriteLine($"DRAW COMMAND PASS assertions={checks} graphics={graphics}; borrowed lifetime, typed resources, transactional passes and zero warm allocations");
        return checks;
    }
}
