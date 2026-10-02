namespace GameAuthoringLab;

/// <summary>Three deterministic readbacks of instance-local edits across both chunk axes.</summary>
internal static class TileMapEditingGraphics
{
    public static int Run()
    {
        int checks = 0;
        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("TILEMAP EDIT GRAPHICS: " + label);
            checks++;
        }

        var root = new AssetRoot();
        var sample = TileMapAsset.LoadAsset(root, "basics.tilemap.json");
        const int width = 18, height = 18;
        var cells = new int[width * height];
        cells[14 * width + 15] = 1; // Replace this red tile with previously unused blue.
        cells[15 * width + 14] = 4; // Flip this asymmetric atlas region horizontally.
        cells[15 * width + 15] = 1; // Remove this tile at the lower-right corner of chunk 0.
        var source = new TileMapDocument
        {
            Kind = "gal-tilemap", Version = 1, Name = "Runtime edit graphics fixture",
            Width = width, Height = height, TileWidth = 24, TileHeight = 24,
            Resources = sample.Source.Resources, Tiles = sample.Source.Tiles,
            Layers = [new TileLayerRecord { Name = "terrain", Order = 0, Cells = cells }]
        };
        var loaded = TileMapAsset.Build(source, root.FilePath("basics.tilemap.json"), root);
        TileMap original = loaded.Map;
        Check(original.AssetKeys.Length == 2, "wall and decor begin unused by cells");

        using var engine = new EngineHost(false, 64, legacyTone: false);
        using var edited = TileMapInstance.CreateEditable(engine, loaded, new(32, 32));
        using var untouched = new TileMapInstance(engine, loaded, new(496, 32));
        var batch = new SpriteBatch(16);
        var camera = new Camera { Zoom = 1 };
        string output = Path.GetFullPath(Environment.GetEnvironmentVariable("GAL_TILEMAP_EDIT_CAPTURE_DIR")
            ?? "evidence/tilemap-edit/visual-jit");
        Directory.CreateDirectory(output);

        void Capture(int frame, int expectedSprites)
        {
            var input = engine.PollInput();
            Check(input.Drawable, "drawable capture viewport");
            var view = TileView.FromCamera(camera, input.Viewport);
            edited.ExtractSprites(batch, view);
            untouched.AppendSprites(batch, view);
            Check(batch.Count == expectedSprites, "updated occupancy and extraction frame " + frame);
            Native.Check(UiNative.Capture(engine.NativeContext, Path.Combine(output, $"frame-{frame:D3}.bmp")),
                "tilemap edit capture");
            engine.Draw(camera, batch.RegionDraws);
            Check(engine.Textures.Count == 1 && engine.TextureCount == 1 && engine.Textures.Loads == 1,
                "all regions and instances retain one atlas upload frame " + frame);
        }

        Capture(0, 6);
        edited.SetCells([
            new(0, 15, 14, 2),
            new(0, 14, 15, 4, 1),
            new(0, 15, 15, 0),
            new(0, 16, 15, 3, 2), // Populate the initially empty chunk to the right.
            new(0, 15, 16, 4, 3)  // Populate the initially empty chunk below.
        ]);
        TileMap changed = edited.Map;
        Check(!ReferenceEquals(changed, original) && ReferenceEquals(loaded.Map, original)
            && ReferenceEquals(untouched.Map, original), "only edited instance publishes a new snapshot");
        Check(changed.AssetKeys.Length == 3, "new atlas aliases replace no-longer-used soil");
        for (int i = 0; i < cells.Length; i++)
            Check(original.Layers[0].Cell(i) == cells[i] && original.Layers[0].Flip(i) == 0,
                "retained source snapshot stays unchanged at " + i);
        Capture(1, 7);

        // Empty both newly occupied chunks and restore all original IDs and flags.
        edited.SetCells([
            new(0, 15, 14, 1),
            new(0, 14, 15, 4),
            new(0, 15, 15, 1),
            new(0, 16, 15, 0),
            new(0, 15, 16, 0)
        ]);
        Check(edited.Map.AssetKeys.Length == 2, "restored snapshot reports only original used aliases");
        Check(changed.Layers[0].Cell(15 * width + 16) == 3
            && changed.Layers[0].Flip(15 * width + 16) == 2
            && changed.Layers[0].Cell(15 * width + 15) == 0,
            "previous edited snapshot survives the next transaction");
        Capture(2, 6);
        Check(engine.GetStats().Frames == 3, "exactly three captured frames");

        edited.Dispose();
        Check(engine.Textures.Count == 1 && engine.TextureCount == 1, "sibling keeps shared atlas alive");
        untouched.Dispose();
        Check(engine.Textures.Count == 0 && engine.TextureCount == 0, "final owner releases atlas");
        Console.WriteLine($"TILEMAP EDIT GRAPHICS PASS assertions={checks} frames=3 textures=0 captures={output}");
        return 0;
    }
}
