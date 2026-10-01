namespace GameAuthoringLab;

internal static class AuthoredSceneDemo
{
    public static int Run(string path, bool headless, int frames)
    {
        var scene = AuthoredScene.LoadFile(path);
        using var engine = new EngineHost(headless, 4096);
        using var textures = new TextureBank(engine, scene.Catalog);
        textures.Sync(scene.World);
        var batch = new SpriteBatch(scene.World.EntityCount) { RegionResolver = textures.ResolveRegion };
        var camera = new Camera { Zoom = 1 };
        int count = 0;
        while (frames == 0 || count < frames)
        {
            var input = engine.Poll();
            if (input.Quit != 0 || (input.Keys & Native.Escape) != 0) break;
            scene.World.ExtractSprites(batch); engine.Draw(camera, batch.RegionDraws); count++;
            if (!headless) Thread.Sleep(1);
        }
        Console.WriteLine($"AUTHORED SCENE PASS file={path} entities={scene.World.EntityCount} frames={count} backend={engine.Backend}");
        return 0;
    }
}
