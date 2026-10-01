namespace GameAuthoringLab;

internal static class ResourceTests
{
    public static int Run(bool graphics = false)
    {
        int checks = 0;
        void Check(bool condition, string label) { checks++; if (!condition) throw new InvalidOperationException("RESOURCE: " + label); }
        string directory = Path.Combine(Path.GetTempPath(), "gal-resources-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var source = new AssetRoot();
            var assets = new AssetRoot(directory);
            Directory.CreateDirectory(Path.Combine(directory, "art"));
            File.Copy(source.Resolve("cell.bmp"), assets.FilePath("art/cell.bmp"));
            File.Copy(source.Resolve("player.bmp"), assets.FilePath("art/other.bmp"));
            if (!graphics) PathsAndAuthoring(source, assets, Check);
            Lifetimes(assets, !graphics, Check);
            Console.WriteLine($"RESOURCE {(graphics ? "GRAPHICS" : "SELF-TEST")} PASS assertions={checks}; {(graphics ? "real BMP upload/release via selected graphics backend" : "CPU preflight and headless lifetime contracts only")}");
            return checks;
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void Reject<T>(Action action, Action<bool, string> check, string label, Func<T, bool>? inspect = null) where T : Exception
    {
        try { action(); }
        catch (T e) { check(inspect?.Invoke(e) ?? true, label); return; }
        throw new InvalidOperationException("RESOURCE accepted: " + label);
    }

    private static void PathsAndAuthoring(AssetRoot source, AssetRoot assets, Action<bool, string> check)
    {
        check(assets.Resolve("art/cell.bmp") == Path.Combine(assets.DirectoryPath, "art", "cell.bmp"), "logical path resolves below captured root");
        foreach (string invalid in new[] { "", " ", "/cell.bmp", "../cell.bmp", "art/../cell.bmp", "art//cell.bmp", "art/./cell.bmp", "art/", "C:/cell.bmp", "file:cell.bmp", "art\\cell.bmp", "bad\0.bmp", "bad\n.bmp", "bad?.bmp", "bad*.bmp", "bad|.bmp", "bad\".bmp", "art./cell.bmp", "art /cell.bmp" })
            Reject<AssetException>(() => assets.Resolve(invalid), check, "reject logical path " + invalid,
                e => e.Code == "ASSET_PATH" && e.Root == assets.DirectoryPath && e.LogicalPath == invalid);
        Reject<AssetException>(() => assets.Resolve(null!), check, "null logical path diagnostic", e => e.Code == "ASSET_PATH");
        Reject<AssetException>(() => assets.Resolve("missing.bmp"), check, "missing resource diagnostic", e => e.Code == "ASSET_MISSING" && e.LogicalPath == "missing.bmp");
        Reject<AssetException>(() => assets.Resolve("art"), check, "directory is not a file", e => e.Code == "ASSET_FILE");
        Reject<AssetException>(() => assets.LogicalPathFor(source.FilePath("cell.bmp")), check, "physical source outside root rejected", e => e.Code == "ASSET_PATH");
        check(assets.LogicalPathFor(assets.FilePath("art/cell.bmp")) == "art/cell.bmp", "physical/logical round trip");
        File.WriteAllBytes(assets.FilePath("art/truncated.bmp"), [0x42, 0x4d]);
        Reject<AssetException>(() => assets.ValidateBitmap("art/truncated.bmp"), check, "truncated BMP diagnostic", e => e.Code == "ASSET_BMP");
        Reject<AssetException>(() => assets.ValidateBitmap("art/cell.png"), check, "unsupported type rejected before load", e => e.Code == "ASSET_BMP");
        var mapping = new Dictionary<string, string> { ["stable-id"] = "art/cell.bmp" };
        var catalog = new AssetCatalog(assets, mapping); mapping["stable-id"] = "missing.bmp";
        check(catalog.Exists("stable-id") && !catalog.Exists("STABLE-ID") && catalog.Paths["stable-id"] == "art/cell.bmp", "catalog copies mapping and keeps case-sensitive IDs");
        Reject<AssetException>(() => catalog.PathFor("unknown"), check, "unregistered key diagnostic", e => e.Code == "ASSET_KEY" && e.LogicalPath == "unknown");

        string? environment = Environment.GetEnvironmentVariable("GAL_ASSET_ROOT");
        string working = Directory.GetCurrentDirectory();
        try
        {
            Environment.SetEnvironmentVariable("GAL_ASSET_ROOT", assets.DirectoryPath);
            check(new AssetCatalog().Root == assets.DirectoryPath && UiProbe.SourcePath == assets.FilePath("ui/settings.rml")
                && GameUiSession.SourcePath == assets.FilePath("ui/game.rml"), "scene sample and UI share environment root policy");
            check(new AssetRoot(source.DirectoryPath).DirectoryPath == source.DirectoryPath, "explicit root overrides environment");
            Directory.SetCurrentDirectory(assets.DirectoryPath);
            check(assets.Resolve("art/cell.bmp") == assets.FilePath("art/cell.bmp"), "captured root ignores later cwd changes");
            Environment.SetEnvironmentVariable("GAL_ASSET_ROOT", null);
            check(new AssetRoot().DirectoryPath == source.DirectoryPath, "packaged assets override working directory fallback");
        }
        finally { Directory.SetCurrentDirectory(working); Environment.SetEnvironmentVariable("GAL_ASSET_ROOT", environment); }

        Directory.CreateDirectory(assets.FilePath("levels"));
        File.Copy(source.Resolve("compositions.scene.json"), assets.FilePath("levels/example.scene.json"));
        File.Copy(source.Resolve("cell.bmp"), assets.FilePath("levels/cell.bmp"));
        var scene = AuthoredScene.LoadAsset(assets, "levels/example.scene.json");
        check(scene.Catalog.LogicalPathFor("cell") == "levels/cell.bmp" && scene.Resources["cell"] == assets.FilePath("levels/cell.bmp"), "root-backed v1 scene retains sibling resource meaning");
        var independent = AuthoredScene.LoadFile(assets.FilePath("levels/example.scene.json"));
        check(independent.Resources["cell"] == scene.Resources["cell"] && !ReferenceEquals(scene.World, independent.World), "standalone/root-backed load agrees and creates independent world");
        string original = AuthoredScene.Write(scene.Source, assets.FilePath("levels/example.scene.json"), assets);
        scene.Source.Resources[0] = new() { Key = "cell", Path = "renamed.bmp" };
        File.Copy(source.Resolve("cell.bmp"), assets.FilePath("levels/renamed.bmp"));
        string renamed = AuthoredScene.Write(scene.Source, assets.FilePath("levels/example.scene.json"), assets);
        var remapped = AuthoredScene.Load(renamed, assets.FilePath("levels/example.scene.json"), assets);
        check(remapped.World.Entities.Where(e => e.Sprite?.AssetKey is not null).All(e => e.Sprite?.AssetKey == "cell")
            && remapped.Catalog.LogicalPathFor("cell") == "levels/renamed.bmp", "physical rename changes mapping without changing logical authored IDs");
        Reject<AuthoredSceneException>(() => AuthoredScene.Load(original.Replace("cell.bmp", "../cell.bmp", StringComparison.Ordinal), assets.FilePath("levels/example.scene.json"), assets), check,
            "nested scene path error preserves source field", e => e.Code == "SCENE_RESOURCE" && e.JsonPath == "$.resources[0].path" && e.InnerException is AssetException);

        Directory.CreateDirectory(assets.FilePath("menus"));
        foreach (string file in new[] { "settings.rml", "settings.rcss", "game.rml", "game.rcss" })
            File.Copy(source.Resolve("ui/" + file), assets.FilePath("menus/" + file));
        check(UiAuthoring.ValidateAsset(assets, "menus/settings.rml").RmlFile == assets.FilePath("menus/settings.rml"), "settings UI uses same root namespace");
        check(GameUiAuthoring.ValidateAsset(assets, "menus/game.rml").Rml.Contains("game-start", StringComparison.Ordinal), "game UI uses same root namespace");
        File.Delete(assets.FilePath("menus/game.rcss"));
        Reject<UiAuthoringException>(() => GameUiAuthoring.ValidateAsset(assets, "menus/game.rml"), check,
            "missing stylesheet retains UI and asset diagnostics", e => e.Code == "UI_FILE" && e.InnerException is AssetException { Code: "ASSET_MISSING", LogicalPath: "menus/game.rcss" });

        if (!OperatingSystem.IsWindows())
        {
            File.CreateSymbolicLink(assets.FilePath("art/link.bmp"), source.FilePath("cell.bmp"));
            Directory.CreateSymbolicLink(assets.FilePath("linked"), source.DirectoryPath);
            Reject<AssetException>(() => assets.Resolve("art/link.bmp"), check, "file symlink rejected", e => e.Code == "ASSET_LINK");
            Reject<AssetException>(() => assets.Resolve("linked/cell.bmp"), check, "directory symlink rejected", e => e.Code == "ASSET_LINK");
            Reject<AuthoredSceneException>(() => AuthoredScene.LoadFile(assets.FilePath("linked/compositions.scene.json"), assets), check,
                "root-backed physical scene read checks source ancestors", e => e.Code == "SCENE_FILE" && e.InnerException is AssetException { Code: "ASSET_LINK" });
            File.CreateSymbolicLink(assets.FilePath("menus/game.rcss"), source.FilePath("ui/game.rcss"));
            Reject<UiAuthoringException>(() => GameUiAuthoring.ValidateAsset(assets, "menus/game.rml"), check,
                "UI cannot bypass shared link policy", e => e.InnerException is AssetException { Code: "ASSET_LINK" });
        }
    }

    private static void Lifetimes(AssetRoot assets, bool headless, Action<bool, string> check)
    {
        var engine = new EngineHost(headless, 64);
        TextureLease? orphan = null;
        try
        {
            var cache = engine.Textures;
            var first = cache.Acquire(assets, "art/cell.bmp");
            var shared = cache.Acquire(assets, "art/cell.bmp");
            check(cache.Count == 1 && cache.Loads == 1 && first.Handle == shared.Handle, "leases share one context-owned texture");
            check(engine.TextureCount == (headless ? 0u : 1u), "headless never reports a native texture");
            var equivalentRoot = new AssetRoot(assets.FilePath("art"));
            using (var alias = cache.Acquire(equivalentRoot, "cell.bmp"))
                check(cache.Count == 1 && alias.Handle == first.Handle, "same physical path shares across root views");
            byte[] bitmap = File.ReadAllBytes(assets.FilePath("art/cell.bmp"));
            File.Delete(assets.FilePath("art/cell.bmp"));
            using (var retained = cache.Acquire(assets, "art/cell.bmp")) check(retained.Handle == first.Handle, "retained snapshot does not reread a deleted file");
            first.Dispose(); first.Dispose();
            check(cache.Count == 1 && cache.Releases == 0, "nonfinal/idempotent release retains resource");
            Reject<ObjectDisposedException>(() => _ = first.Handle, check, "disposed lease cannot expose stale handle");
            Reject<InvalidOperationException>(() => Task.Run(() => _ = shared.Handle).GetAwaiter().GetResult(), check, "wrong-thread handle access rejected");
            Reject<InvalidOperationException>(() => Task.Run(shared.Dispose).GetAwaiter().GetResult(), check, "wrong-thread release retains lease");
            shared.Dispose();
            check(cache.Count == 0 && cache.Releases == 1 && engine.TextureCount == 0, "last lease releases immediately");
            Reject<AssetException>(() => cache.Acquire(assets, "art/cell.bmp"), check, "new acquire observes deleted source", e => e.Code == "ASSET_MISSING");
            File.WriteAllBytes(assets.FilePath("art/cell.bmp"), bitmap);

            var catalog = new AssetCatalog(assets, new Dictionary<string, string>
            {
                ["a"] = "art/cell.bmp", ["alias"] = "art/cell.bmp", ["b"] = "art/other.bmp", ["missing"] = "art/missing.bmp", ["bad"] = "art/bad.bmp"
            });
            using var firstBank = new TextureBank(engine, catalog);
            using var secondBank = new TextureBank(engine, catalog);
            var world = WorldWith("a", "alias"); firstBank.Sync(world); secondBank.Sync(world);
            check(firstBank.LoadedCount == 2 && secondBank.LoadedCount == 2 && cache.Count == 1 && cache.Loads == 2, "overlapping worlds and key aliases share uploads");
            int loads = cache.Loads;
            for (int i = 0; i < 5; i++) firstBank.Sync(world);
            check(cache.Loads == loads, "unchanged sync never reloads");
            Reject<AssetException>(() => firstBank.Sync(WorldWith("b", "missing")), check, "failed candidate validates before uploads", e => e.Code == "ASSET_MISSING");
            check(firstBank.LoadedCount == 2 && cache.Count == 1 && cache.Loads == loads && firstBank.Resolve("a") == secondBank.Resolve("alias"), "failed sync retains all prior leases");
            Reject<InvalidOperationException>(() => Task.Run(() => firstBank.Sync(world)).GetAwaiter().GetResult(), check, "wrong-thread sync rejected even headless");
            firstBank.Sync(WorldWith("b"));
            check(cache.Count == 2 && firstBank.LoadedCount == 1 && secondBank.LoadedCount == 2, "one world replacement retains another world's lease");
            firstBank.Sync(new World()); firstBank.Dispose(); firstBank.Dispose();
            check(cache.Count == 1, "empty world releases only its own resources");
            Reject<ObjectDisposedException>(() => firstBank.Sync(world), check, "disposed bank cannot resurrect");
            Reject<ObjectDisposedException>(() => firstBank.Resolve("b"), check, "disposed bank resolution rejected");

            if (!headless)
            {
                byte[] bad = (byte[])bitmap.Clone();
                System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(bad.AsSpan(28), 7);
                File.WriteAllBytes(assets.FilePath("art/bad.bmp"), bad);
                check(assets.ValidateBitmap("art/bad.bmp").Length > 0, "CPU header preflight deliberately does not decode pixels");
                loads = cache.Loads;
                Reject<AssetException>(() => secondBank.Sync(WorldWith("b", "bad")), check, "native decode failure has resource identity", e => e.Code == "ASSET_UPLOAD" && e.LogicalPath == "art/bad.bmp");
                check(cache.Count == 1 && cache.Loads == loads + 1 && engine.TextureCount == 1 && secondBank.LoadedCount == 2, "upload failure rolls back completed candidate upload and retains live world");
                var batch = new SpriteBatch(4) { TextureResolver = secondBank.Resolve }; world.ExtractSprites(batch);
                engine.Draw(new Camera { Zoom = 1 }, batch.Draws);
                check(engine.GetStats().Frames == 1, "previous world still draws after failed replacement");
            }
            secondBank.Dispose();
            check(cache.Count == 0 && engine.TextureCount == 0 && cache.Loads == cache.Releases, "balanced lifetime cleanup");
            if (headless)
            {
                Directory.CreateDirectory(assets.FilePath("capacity"));
                var leases = new List<TextureLease>();
                try
                {
                    for (int i = 0; i < TextureCache.MaximumTextures; i++)
                    {
                        string path = $"capacity/{i}.bmp";
                        File.WriteAllBytes(assets.FilePath(path), bitmap);
                        leases.Add(cache.Acquire(assets, path));
                    }
                    check(cache.Count == TextureCache.MaximumTextures, "headless cache enforces the native-sized capacity contract");
                    Reject<AssetException>(() => cache.Acquire(assets, "art/cell.bmp"), check, "capacity failure preserves entries", e => e.Code == "ASSET_CAPACITY");
                    using (var retained = cache.Acquire(assets, "capacity/0.bmp"))
                        check(cache.Count == TextureCache.MaximumTextures, "retaining an existing entry works at capacity");
                }
                finally { foreach (var lease in leases) lease.Dispose(); }
                check(cache.Count == 0 && cache.Loads == cache.Releases, "capacity probe cleanup is balanced");
            }
            orphan = cache.Acquire(assets, "art/cell.bmp");
            engine.Dispose();
            Reject<ObjectDisposedException>(() => _ = orphan.Handle, check, "engine destruction invalidates outstanding lease");
            Reject<ObjectDisposedException>(() => cache.Acquire(assets, "art/cell.bmp"), check, "destroyed engine cache cannot load");
        }
        finally { engine.Dispose(); }
        using var replacement = new EngineHost(headless, 64);
        using var replacementLease = replacement.Textures.Acquire(assets, "art/cell.bmp");
        orphan!.Dispose(); orphan.Dispose();
        check(replacement.Textures.Count == 1 && replacement.TextureCount == (headless ? 0u : 1u), "late old-context lease disposal cannot release a new context resource");
    }

    private static World WorldWith(params string[] keys)
    {
        var world = new World(); var scene = world.CreateScene("resource test");
        foreach (string key in keys) world.Create(key, scene).Sprite = new Sprite2D(24, 24, AssetKey: key);
        return world;
    }
}
