using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
namespace GameAuthoringLab;

internal static unsafe class RegionTests
{
    public static int Run(bool graphics = false)
    {
        int count = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception("REGION: " + label); count++; }
        void Reject<T>(Action action, string label) where T : Exception
        { try { action(); } catch (T) { count++; return; } throw new Exception("REGION accepted: " + label); }
        Check(sizeof(SpriteDrawV2) == 88 && sizeof(TextureInfo) == 16
            && Marshal.OffsetOf<SpriteDrawV2>(nameof(SpriteDrawV2.Draw)).ToInt32() == 8
            && Marshal.OffsetOf<SpriteDrawV2>(nameof(SpriteDrawV2.SourceX)).ToInt32() == 64, "v2 C layout");
        var root = new AssetRoot();
        var source = AuthoredScene.LoadAsset(root, "regions.scene.json");
        Check(source.Catalog.LogicalPathFor("left") == source.Catalog.LogicalPathFor("right"), "IDs independent of shared atlas filename");
        Check(source.Catalog.TextureFor("right").Region == new TextureRegion(8, 0, 8, 8), "authored region retained");
        Check(AuthoredScene.Load(AuthoredScene.Write(source.Source, root.FilePath("regions.scene.json"), root), root.FilePath("regions.scene.json"), root).Catalog.TextureFor("left").Region == new TextureRegion(0,0,8,8), "authored sourcegen write/load region roundtrip");
        var restored = ScenePersistence.Load(ScenePersistence.Save(source.World), source.Catalog.Exists).World;
        Check(ScenePersistence.Save(restored) == ScenePersistence.Save(source.World), "v2 flips stable save roundtrip");
        Check(JsonNode.Parse(ScenePersistence.Save(restored))!["version"]!.GetValue<int>() == 2, "flips select v2 snapshot");
        string plain = ScenePersistence.Save(new RoomGame().World);
        Check(JsonNode.Parse(plain)!["version"]!.GetValue<int>() == 1 && !plain.Contains("flipX", StringComparison.Ordinal), "unchanged snapshots keep v1 bytes/schema");
        string json = File.ReadAllText(root.Resolve("regions.scene.json"));
        var doc = JsonNode.Parse(json)!; doc["version"] = 1;
        Reject<AuthoredSceneException>(() => AuthoredScene.Load(doc.ToJsonString(), root.FilePath("regions.scene.json"), root), "v1 cannot reinterpret region");
        doc = JsonNode.Parse(json)!; doc["resources"]![0]!["region"]!["width"] = 17;
        try { AuthoredScene.Load(doc.ToJsonString(), root.FilePath("regions.scene.json"), root); throw new Exception("Invalid region accepted"); }
        catch (AuthoredSceneException e) { Check(e.Code == "SCENE_RESOURCE" && e.JsonPath == "$.resources[0].region", "exact resource diagnostic"); }
        doc = JsonNode.Parse(json)!; doc["resources"]![0]!["region"]!.AsObject().Remove("width");
        Reject<AuthoredSceneException>(() => AuthoredScene.Load(doc.ToJsonString(), root.FilePath("regions.scene.json"), root), "sourcegen requires full rectangle");
        doc = JsonNode.Parse(ScenePersistence.Save(restored))!; doc["version"] = 1;
        Reject<SceneFormatException>(() => ScenePersistence.Load(doc.ToJsonString(), _ => true), "v1 rejects nondefault flips");
        foreach (var invalid in new[] { new TextureRegion(-1,0,1,1), new TextureRegion(0,0,0,1), new TextureRegion(15,0,2,1), new TextureRegion(int.MaxValue,0,int.MaxValue,1) })
            Reject<ArgumentOutOfRangeException>(() => invalid.Validate(16,8), "invalid or overflowing source");
        using var engine = new EngineHost(!graphics, 64);
        using var bank = new TextureBank(engine, source.Catalog);
        bank.Sync(restored);
        Check(bank.LoadedCount == 4 && engine.Textures.Count == 1 && engine.Textures.Loads == 1, "four IDs share one texture upload/cache entry");
        Check(bank.Resolve("left") == bank.Resolve("right"), "same handle across regions");
        using (var lease = engine.Textures.Acquire(root, "regions.bmp"))
            Check(lease.Info.Width == 16 && lease.Info.Height == 8, "retained decoded/preflight dimensions");
        var batch = new SpriteBatch(16) { RegionResolver = bank.ResolveRegion };
        restored.ExtractSprites(batch);
        Check(batch.RegionDraws.Length == 8 && batch.RegionDraws[2].Flags == 1 && batch.RegionDraws[3].Flags == 2, "flip bits extracted without geometric changes");
        Check(batch.RegionDraws[0].Draw.X == 32 && batch.RegionDraws[4].Draw.M12 > .99f, "rotation and stable authored order");
        Reject<InvalidOperationException>(() => { _ = batch.Draws.Length; }, "legacy affine accessor cannot silently lose regions");
        if (graphics)
        {
            var info = engine.GetTextureInfo(bank.Resolve("left"));
            Check(info.Width == 16 && info.Height == 8, "native decoded size query");
            Check(batch.RegionDraws[0].SourceWidth == 8 && batch.RegionDraws[1].SourceX == 8, "texel coordinates reach native ABI");
            engine.PollInput();
            string output = Path.GetFullPath(Environment.GetEnvironmentVariable("GAL_REGION_CAPTURE") ?? "evidence/regions/fixture.bmp");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            Native.Check(UiNative.Capture(engine.NativeContext, output), "region capture");
        }
        var camera = new Camera { Zoom = 1 };
        engine.Draw(camera, batch.RegionDraws);
        Check(engine.GetStats().Sprites == 8 && engine.GetStats().DrawCalls == (graphics ? 1u : 0u), "adjacent regions batch by texture without reordering");
        if (!graphics)
        {
            for (int i=0;i<128;i++) { bank.Sync(restored); restored.ExtractSprites(batch); engine.Draw(camera,batch.RegionDraws); }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i=0;i<1000;i++) { bank.Sync(restored); restored.ExtractSprites(batch); engine.Draw(camera,batch.RegionDraws); }
            Check(GC.GetAllocatedBytesForCurrentThread() == before, "zero allocation warmed region extraction/sync/submit");
        }
        var sortedWorld = new World(); var sortedScene = sortedWorld.CreateScene("sort");
        var high = sortedWorld.Create("high", sortedScene, new Transform2D(30,0)); high.Sprite = new(1,1,AssetKey:"left",Layer:2,FlipX:true);
        var low = sortedWorld.Create("low", sortedScene, new Transform2D(10,0)); low.Sprite = new(1,1,AssetKey:"right",Layer:-1,FlipY:true);
        var tie = sortedWorld.Create("tie", sortedScene, new Transform2D(20,0)); tie.Sprite = new(1,1,AssetKey:"left",Layer:-1);
        sortedWorld.ExtractSprites(batch);
        Check(batch.RegionDraws[0].Draw.X == 10 && batch.RegionDraws[0].Flags == 2 && batch.RegionDraws[1].Draw.X == 20 && batch.RegionDraws[2].Flags == 1, "stable layer sorting keeps geometry, regions and flags together");
        var remapped = new AssetCatalog(root, new Dictionary<string, TextureAsset> { ["left"] = new("regions.bmp", new(8,0,8,8)) });
        using (var remappedBank = new TextureBank(engine, remapped))
        {
            var world = new World(); var remapScene = world.CreateScene("remap"); var sameId = Guid.NewGuid();
            var e = world.Create("logical left", remapScene, persistentId:sameId); e.Sprite = new(1,1,AssetKey:"left");
            remappedBank.Sync(world);
            Check(e.PersistentId == sameId && remappedBank.ResolveRegion("left").Region == new TextureRegion(8,0,8,8) && engine.Textures.Loads == 1, "retargeting region preserves authored key/identity and shared upload");
        }
        var invalidCatalog = new AssetCatalog(root, new Dictionary<string, TextureAsset>
        { ["good"] = new("regions.bmp",new(0,0,8,8)), ["bad"] = new("regions.bmp",new(15,0,2,1)) });
        using var candidateBank = new TextureBank(engine, invalidCatalog);
        var candidate = new World(); var scene = candidate.CreateScene("candidate");
        var entity = candidate.Create("candidate",scene); entity.Sprite = new(1,1,AssetKey:"good"); candidateBank.Sync(candidate);
        entity.Sprite = new(1,1,AssetKey:"bad");
        Reject<AssetException>(() => candidateBank.Sync(candidate), "bad region retains previous usable leases");
        Check(candidateBank.LoadedCount == 1 && candidateBank.Resolve("good") == bank.Resolve("left"), "candidate failure retains shared resident texture");
        candidateBank.Dispose(); bank.Dispose();
        Check(engine.Textures.Count == 0 && engine.TextureCount == 0, "all leases and native textures released");
        Console.WriteLine($"REGION {(graphics ? "GRAPHICS" : "SELF-TEST")} PASS assertions={count}");
        return count;
    }
}
