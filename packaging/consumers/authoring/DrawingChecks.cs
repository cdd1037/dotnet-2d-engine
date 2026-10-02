using System.Numerics;
using GameAuthoringLab;

internal static class DrawingChecks
{
    private static readonly Camera Camera = new() { Zoom = 1 };
    private static SpriteCommand Solid => new(Transform2D.Identity, new(8, 8), Vector4.One);

    internal static void Run(Checks check, EngineHost engine, AssetRoot assets)
    {
        using var texture = engine.Textures.Acquire(assets, "palette.bmp");
        using var material = engine.Materials.Acquire(assets, "tint.material.json");
        using var target = engine.RenderTargets.Create(16, 16);
        SpriteCommand[] commands =
        [
            Solid,
            new(new(2, 3, Rotation: .2f), new(8, 4), new(1, .5f, .25f, 1), texture.Texture)
                { Region = new(1, 0, 1, 1), FlipX = true, FlipY = true,
                  Material = material.Material, Parameters = new(new(.5f, 1, 1, 1)) },
            new(new(20, 0), new(16, 16), Vector4.One, target.Texture)
        ];
        FramePass[] passes = [FramePass.ToTarget(target.Target, Camera, 0, 2), FramePass.Window(Camera, 2, 1)];
        uint frames = engine.GetStats().Frames;
        engine.Draw(Camera, commands.AsSpan(0, 2));
        engine.RenderFrame(passes, commands);
        var world = new World();
        world.Create("Caller solid").Sprite = new(6, 6);
        var batch = new SpriteBatch(1);
        world.ExtractSprites(batch);
        engine.Draw(Camera, batch);
        engine.DrawWithOverlay(Camera, batch, commands.AsSpan(0, 2));
        check.That(engine.GetStats().Frames == frames + 4 && batch.Count == 1,
            "solid, atlas/flips/material, sampled target, batch and overlay submit through public APIs");
        check.That(texture.Info.Width == 2 && texture.Info.Height == 1 && target.Width == 16,
            "consumer assets and owned target retain their dimensions");
        frames = engine.GetStats().Frames;
        engine.Draw(Camera, new[] { Solid with { Size = new(0, 8) }, Solid with { Size = new(8, 0) }, Solid with { Size = Vector2.Zero } });
        check.That(engine.GetStats().Frames == frames + 1, "finite zero extents remain valid for empty bars and sprites");

        frames = engine.GetStats().Frames;
        check.Reject<ArgumentException>(() => engine.Draw(Camera, new[] { Solid with { Region = new(0, 0, 1, 1) } }), "region on a default texture");
        check.Reject<ArgumentException>(() => engine.Draw(Camera, new[] { Solid with { Parameters = new(Vector4.One) } }), "parameters on a default material");
        check.Reject<ArgumentOutOfRangeException>(() => engine.Draw(Camera, new[] { commands[1] with { Region = new(2, 0, 1, 1) } }), "atlas region outside its image");
        check.Reject<ArgumentOutOfRangeException>(() => engine.Draw(Camera, new[] { Solid with { Size = new(-1, 8) } }), "negative sprite width");
        check.Reject<ArgumentOutOfRangeException>(() => engine.Draw(Camera, new[] { Solid with { Tint = new(float.NaN, 1, 1, 1) } }), "nonfinite color");
        check.Reject<ArgumentException>(() => engine.RenderFrame(new[] { default(FramePass) }, Array.Empty<SpriteCommand>()), "unconfigured pass");
        check.Reject<ArgumentException>(() => engine.RenderFrame(new[] { FramePass.ToTarget(default, Camera, 0, 0), FramePass.Window(Camera, 0, 0) }, Array.Empty<SpriteCommand>()), "default render attachment");
        check.Reject<InvalidOperationException>(() => engine.RenderFrame(
            new[] { FramePass.ToTarget(target.Target, Camera, 0, 1), FramePass.Window(Camera, 1, 0) },
            new[] { Solid with { Texture = target.Texture } }), "same-pass target feedback");
        check.Reject<InvalidOperationException>(() => engine.RenderFrame(
            new[] { FramePass.Window(Camera, 1, 1) }, new[] { Solid, Solid }), "non-partitioning pass range");
        check.That(engine.GetStats().Frames == frames, "invalid managed commands and native pass validation commit no frame");

        var borrowedWorld = new World();
        borrowedWorld.Create("late").Sprite = new(4, 4, AssetKey: "image", Layer: 2);
        borrowedWorld.Create("early").Sprite = new(4, 4, AssetKey: "target", Layer: 1);
        var borrowedBatch = new SpriteBatch(2) { RegionResolver = key => key == "image"
            ? new(texture.Texture, new(0, 0, 1, 1)) : target.Binding };
        borrowedWorld.ExtractSprites(borrowedBatch);
        var debug = new DebugDrawBuffer(4, new(texture.Texture, new(0, 0, 1, 1))) { Enabled = true };
        check.That(debug.TryAddLine(0, 0, 10, 10, 1), "typed debug binding builds reusable geometry");
        check.That(borrowedBatch.Commands.Length == 2 && borrowedBatch.Commands[0].Texture == target.Texture
            && borrowedBatch.Commands[1].Texture == texture.Texture, "sorted extraction preserves typed resource order");
        SpriteCommand[] extracted = new SpriteCommand[3]; borrowedBatch.Commands.CopyTo(extracted);
        extracted[2] = Solid with { Material = material.Material };
        using (var composite = engine.RenderTargets.Create(8, 8))
        {
            extracted[2] = extracted[2] with { Texture = composite.Texture };
            engine.RenderFrame(new[] { FramePass.ToTarget(composite.Target, Camera, 0, 2), FramePass.Window(Camera, 2, 1) }, extracted);
        }
        engine.Draw(Camera, borrowedBatch, new[] { FramebufferClip.Disabled });
        engine.DrawWithOverlay(Camera, borrowedBatch, debug);
        engine.Draw(Camera, debug);
        check.That(debug.Commands.Length == 1 && debug.Commands[0].Texture == texture.Texture,
            "debug semantic view keeps its borrowed texture for explicit material/pass composition");
        SpriteCommand[] debugCommands = [debug.Commands[0] with { Material = material.Material }];
        using (var debugTarget = engine.RenderTargets.Create(8, 8))
            engine.RenderFrame(new[] { FramePass.ToTarget(debugTarget.Target, Camera, 0, 1), FramePass.Window(Camera, 1, 0) }, debugCommands,
                new[] { FramebufferClip.Disabled });

        // The sibling keeps the cache entry alive, but not the original borrowed view.
        using var textureSibling = engine.Textures.Acquire(assets, "palette.bmp");
        TextureHandle copiedTexture = texture.Texture;
        SpriteCommand copiedTextureCommand = Solid with { Texture = copiedTexture };
        texture.Dispose();
        check.That(engine.Textures.Count == 1, "retained sibling keeps image resident");
        check.Reject<ObjectDisposedException>(() => engine.Draw(Camera, new[] { copiedTextureCommand }), "copied texture view after its lease is disposed");
        frames = engine.GetStats().Frames;
        check.Reject<ObjectDisposedException>(() => engine.Draw(Camera, borrowedBatch), "extracted typed texture is invalidated with the exact lease after sorting");
        check.Reject<ObjectDisposedException>(() => engine.DrawWithOverlay(Camera, borrowedBatch, commands.AsSpan(0, 1)), "stale batch is rejected before overlay frame begin");
        check.Reject<ObjectDisposedException>(() => engine.Draw(Camera, debug), "debug geometry cannot outlive its borrowed texture");
        check.Reject<ObjectDisposedException>(() => engine.Draw(Camera, debug.Commands), "typed debug view retains exact borrowed lifetime");
        check.That(engine.GetStats().Frames == frames, "invalid borrowed batch and debug resources commit no frame");
        debug.Enabled = false;
        engine.Draw(Camera, debug);
        check.That(engine.GetStats().Frames == frames + 1 && debug.Commands.IsEmpty,
            "disabled debug buffer submits no stale resource and exposes no prior geometry");
        engine.Draw(Camera, new[] { Solid with { Texture = textureSibling.Texture } });
        using var materialSibling = engine.Materials.Acquire(assets, "tint.material.json");
        MaterialHandle copiedMaterial = material.Material;
        SpriteCommand copiedMaterialCommand = Solid with { Material = copiedMaterial };
        material.Dispose();
        check.That(engine.Materials.Count == 1, "retained sibling keeps material resident");
        check.Reject<ObjectDisposedException>(() => engine.Draw(Camera, new[] { copiedMaterialCommand }), "copied material view after its lease is disposed");
        engine.Draw(Camera, new[] { Solid with { Material = materialSibling.Material } });

        TextureHandle copiedSample = target.Texture;
        RenderTargetHandle copiedAttachment = target.Target;
        FramePass copiedPass = FramePass.ToTarget(copiedAttachment, Camera, 0, 0);
        target.Dispose();
        check.Reject<ObjectDisposedException>(() => engine.Draw(Camera, new[] { Solid with { Texture = copiedSample } }), "copied sampling view after target disposal");
        check.Reject<ObjectDisposedException>(() => engine.RenderFrame(new[] { copiedPass, FramePass.Window(Camera, 0, 0) }, Array.Empty<SpriteCommand>()), "copied pass after target disposal");
        engine.Draw(Camera, new[] { Solid });
        check.That(engine.RenderTargets.Count == 0, "rejected stale submissions leave the engine usable");

    }

    internal static void CheckPreviousEngine(Checks check, AssetRoot assets)
    {
        // Native contexts are sequential. Keep views from a disposed context and
        // prove that a new context can never accept them, even if native IDs recur.
        using var ended = EngineHost.Create(headless: true, maxSprites: 4);
        using var endedTexture = ended.Textures.Acquire(assets, "palette.bmp");
        using var endedMaterial = ended.Materials.Acquire(assets, "tint.material.json");
        using var endedTarget = ended.RenderTargets.Create(4, 4);
        TextureHandle engineTexture = endedTexture.Texture, engineSample = endedTarget.Texture;
        MaterialHandle engineMaterial = endedMaterial.Material;
        RenderTargetHandle engineTarget = endedTarget.Target;
        var oldWorld = new World(); oldWorld.Create("old").Sprite = new(2, 2, AssetKey: "old");
        var oldBatch = new SpriteBatch(1) { RegionResolver = _ => new(engineTexture) };
        oldWorld.ExtractSprites(oldBatch);
        InputFrame copiedInput = ended.PollInputFrame();
        ended.Dispose();
        using var replacement = EngineHost.Create(headless: true, maxSprites: 4);
        check.Reject<ObjectDisposedException>(() => replacement.Draw(Camera, new[] { Solid with { Texture = engineTexture } }), "texture from a previous engine");
        check.Reject<ObjectDisposedException>(() => replacement.Draw(Camera, new[] { Solid with { Material = engineMaterial } }), "material from a previous engine");
        check.Reject<ObjectDisposedException>(() => replacement.Draw(Camera, new[] { Solid with { Texture = engineSample } }), "sampled target from a previous engine");
        check.Reject<ObjectDisposedException>(() => replacement.RenderFrame(new[] { FramePass.ToTarget(engineTarget, Camera, 0, 0), FramePass.Window(Camera, 0, 0) }, Array.Empty<SpriteCommand>()), "attachment from a previous engine");
        check.Reject<ObjectDisposedException>(() => replacement.Draw(Camera, oldBatch), "extracted batch from a previous engine");
        check.That(replacement.GetStats().Frames == 0, "prior-engine resource rejection opens no frame in the replacement");
        replacement.Draw(Camera, new[] { Solid });
        check.That(replacement.GetStats().Frames == 1, "replacement remains usable after rejecting previous-engine views");
        check.That(!copiedInput.Game.KeyDown(PhysicalKey.Space) && !copiedInput.Raw.ButtonDown(PointerButton.Left),
            "copied safe input remains readable after engine disposal");
    }

    internal static long MeasureWarm(EngineHost engine, AssetRoot assets)
    {
        using var texture = engine.Textures.Acquire(assets, "palette.bmp");
        using var material = engine.Materials.Acquire(assets, "tint.material.json");
        using var target = engine.RenderTargets.Create(8, 8);
        SpriteCommand[] commands = [Solid with { Texture = texture.Texture, Region = new(0, 0, 1, 1), Material = material.Material }, Solid with { Texture = target.Texture }];
        FramePass[] passes = [FramePass.ToTarget(target.Target, Camera, 0, 1), FramePass.Window(Camera, 1, 1)];
        var world = new World();
        world.Create("Warm caller sprite").Sprite = new(2, 2, AssetKey: "warm");
        var batch = new SpriteBatch(1) { RegionResolver = _ => new(texture.Texture, new(0, 0, 1, 1)) };
        var debug = new DebugDrawBuffer(1, new(texture.Texture)) { Enabled = true };
        debug.TryAddLine(0, 0, 1, 1, 1);
        world.ExtractSprites(batch);
        FramePass[] batchPasses = [FramePass.Window(Camera, 0, 1)];
        void Frame()
        {
            engine.Draw(Camera, commands);
            engine.Draw(Camera, batch);
            engine.DrawWithOverlay(Camera, batch, commands);
            engine.RenderFrame(batchPasses, batch.Commands);
            engine.DrawWithOverlay(Camera, batch, debug);
            engine.Draw(Camera, debug);
            engine.Draw(Camera, debug.Commands);
            engine.RenderFrame(passes, commands);
        }
        for (int i = 0; i < 256; i++) Frame();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1024; i++) Frame();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
