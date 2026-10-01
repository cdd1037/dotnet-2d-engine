namespace GameAuthoringLab;

internal static class AuthoredSceneDemo
{
    public static int Run(string path, bool headless, int frames)
    {
        var scene = AuthoredScene.LoadFile(path);
        using var engine = new EngineHost(headless, 4096);
        var textures = new Dictionary<string, ulong>(StringComparer.Ordinal);
        try
        {
            // Upload only used resources; on a failure all completed uploads are released.
            foreach (var entity in scene.World.Entities)
                if (entity.Sprite?.AssetKey is {} key && !textures.ContainsKey(key))
                    textures.Add(key, headless ? 0 : engine.LoadTexture(scene.Resources[key]));
            var batch = new SpriteBatch(scene.World.EntityCount) { TextureResolver = key => textures[key] };
            var camera = new Camera { Zoom = 1 };
            int count = 0;
            while (frames == 0 || count < frames)
            {
                var input = engine.Poll();
                if (input.Quit != 0 || (input.Keys & Native.Escape) != 0) break;
                scene.World.ExtractSprites(batch); engine.Draw(camera, batch.Draws); count++;
                if (!headless) Thread.Sleep(1);
            }
            Console.WriteLine($"AUTHORED SCENE PASS file={path} entities={scene.World.EntityCount} frames={count} backend={engine.Backend}");
            return 0;
        }
        finally { if (!headless) foreach (ulong texture in textures.Values) engine.ReleaseTexture(texture); }
    }
}
