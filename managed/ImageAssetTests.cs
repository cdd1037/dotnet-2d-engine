using System.Buffers.Binary;

namespace GameAuthoringLab;

// Header-only CPU tests. Plausible metadata intentionally need not decode pixels;
// real encoded fixtures and transactional native decode failures belong to graphics tests.
internal static class ImageAssetTests
{
    public static int Run()
    {
        int checks = 0;
        void Check(bool condition, string label) { checks++; if (!condition) throw new InvalidOperationException("IMAGE: " + label); }
        void Reject(Action action, string label, string? code = null)
        {
            try { action(); }
            catch (AssetException e) { Check(code is null || e.Code == code, label + " diagnostic: " + e.Code); return; }
            throw new InvalidOperationException("IMAGE accepted: " + label);
        }
        string directory = Path.Combine(Path.GetTempPath(), "gal-images-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var assets = new AssetRoot(directory);
        try
        {
            void Put(string path, byte[] bytes) => File.WriteAllBytes(assets.FilePath(path), bytes);
            byte[] png = Png(17, 9), jpeg = Jpeg(23, 11), bmp = Bmp(13, 7);
            foreach (var (path, bytes, width, height) in new[]
            {
                ("sample.bmp", bmp, 13, 7), ("sample.BMP", bmp, 13, 7),
                ("sample.png", png, 17, 9), ("sample.PNG", png, 17, 9),
                ("sample.jpg", jpeg, 23, 11), ("sample.JPEG", jpeg, 23, 11)
            })
            {
                Put(path, bytes);
                var info = assets.ReadImageInfo(path);
                var image = ImageAsset.Read(assets, path);
                Check(info.Width == width && info.Height == height && info.Path == assets.FilePath(path), "shared image dimensions " + path);
                Check(image.Info == info && image.EncodedLength == bytes.Length && image.DecodedBytes == (long)width * height * 4
                    && image.Bytes.SequenceEqual(bytes), "bounded immutable snapshot " + path);
                Check(assets.ValidateImage(path) == info.Path, "image path compatibility " + path);
            }
            Check(assets.ReadBitmapInfo("sample.bmp").Width == 13 && assets.ValidateBitmap("sample.bmp") == assets.FilePath("sample.bmp"), "BMP-only API retained");
            Reject(() => assets.ReadBitmapInfo("sample.png"), "legacy BMP API rejects PNG", "ASSET_BMP");
            Reject(() => assets.ValidateBitmap("sample.jpg"), "legacy BMP API rejects JPEG", "ASSET_BMP");
            foreach (string unsupported in new[] { "sample.webp", "sample.gif", "sample.svg", "sample", "sample.bmp.pngx" })
                Reject(() => assets.ReadImageInfo(unsupported), "closed extension profile " + unsupported, "ASSET_IMAGE_FORMAT");
            Reject(() => assets.ReadImageInfo("../sample.png"), "image traversal", "ASSET_PATH");
            Reject(() => ImageAsset.Read(assets, "missing.png"), "snapshot missing source", "ASSET_MISSING");
            foreach (var (path, bytes) in new[] { ("mismatch.png", jpeg), ("mismatch.jpg", png), ("mismatch.bmp", png) })
            {
                Put(path, bytes);
                Reject(() => assets.ReadImageInfo(path), "extension/signature mismatch " + path, "ASSET_IMAGE_FORMAT");
                Reject(() => ImageAsset.Read(assets, path), "snapshot extension/signature mismatch " + path, "ASSET_IMAGE_FORMAT");
            }
            Put("mismatch.bmp", new byte[54]);
            Reject(() => assets.ReadBitmapInfo("mismatch.bmp"), "legacy signature diagnostic", "ASSET_BMP");

            foreach (var (path, bytes, minimum) in new[] { ("short.bmp", bmp, 54), ("short.png", png, png.Length), ("short.jpg", jpeg, jpeg.Length - 2) })
                for (int i = 0; i < minimum; i++)
                {
                    Put(path, bytes[..i]);
                    Reject(() => assets.ReadImageInfo(path), "truncated image " + path + " bytes=" + i);
                }
            byte[] modified = Png(17, 9);
            BinaryPrimitives.WriteUInt32BigEndian(modified.AsSpan(8), uint.MaxValue);
            Put("bad.png", modified); Reject(() => assets.ReadImageInfo("bad.png"), "overflow PNG chunk length", "ASSET_IMAGE");
            modified = Png(17, 9); BinaryPrimitives.WriteUInt32BigEndian(modified.AsSpan(8), 12);
            Put("bad.png", modified); Reject(() => assets.ReadImageInfo("bad.png"), "wrong IHDR length", "ASSET_IMAGE");
            foreach (var (offset, value, label) in new[] { (24, 16, "16-bit PNG"), (25, 1, "unknown PNG color"), (26, 1, "PNG compression"), (27, 1, "PNG filter"), (28, 2, "PNG interlace") })
            {
                modified = Png(17, 9); modified[offset] = (byte)value;
                Put("bad.png", modified); Reject(() => assets.ReadImageInfo("bad.png"), label, "ASSET_IMAGE");
            }
            Put("bad.png", [.. png, 0]); Reject(() => assets.ReadImageInfo("bad.png"), "PNG trailing bytes", "ASSET_IMAGE");
            Put("bad.png", [.. png[..33], .. png[8..33], .. png[33..]]);
            Reject(() => assets.ReadImageInfo("bad.png"), "duplicate PNG IHDR", "ASSET_IMAGE");
            Put("bad.png", [.. png[..33], .. png[^12..]]);
            Reject(() => assets.ReadImageInfo("bad.png"), "missing PNG IDAT", "ASSET_IMAGE");
            foreach (string extension in new[] { "acTL", "CgBI" })
            {
                using var stream = new MemoryStream(); stream.Write(png.AsSpan(0, 33)); Chunk(stream, extension, []); stream.Write(png.AsSpan(33));
                Put("bad.png", stream.ToArray()); Reject(() => assets.ReadImageInfo("bad.png"), "unsupported PNG extension " + extension, "ASSET_IMAGE");
            }
            foreach (byte marker in new byte[] { 0xc0, 0xc1, 0xc2 })
            {
                Put("frame.jpg", Jpeg(23, 11, marker));
                Check(assets.ReadImageInfo("frame.jpg").Width == 23, "baseline/extended/progressive JPEG metadata");
            }
            foreach (byte[] invalid in new byte[][]
            {
                [0xff, 0xd8, 0xff], [0xff, 0xd8, 0xff, 0xff], [0xff, 0xd8, 0x00],
                [0xff, 0xd8, 0xff, 0xe0, 0, 0], [0xff, 0xd8, 0xff, 0xe0, 0, 1],
                [0xff, 0xd8, 0xff, 0xe0, 0xff, 0xff], [0xff, 0xd8, 0xff, 0xda, 0, 2],
                [0xff, 0xd8, 0xff, 0xd8], [0xff, 0xd8, 0xff, 0xd9], [0xff, 0xd8, 0xff, 0xd0]
            })
            { Put("bad.jpg", invalid); Reject(() => assets.ReadImageInfo("bad.jpg"), "invalid JPEG marker/length", "ASSET_IMAGE"); }
            modified = Jpeg(23, 11); modified[12] = 12;
            Put("bad.jpg", modified); Reject(() => assets.ReadImageInfo("bad.jpg"), "JPEG precision", "ASSET_IMAGE");
            modified = Jpeg(23, 11); modified[17] = 2;
            Put("bad.jpg", modified); Reject(() => assets.ReadImageInfo("bad.jpg"), "JPEG component count/length", "ASSET_IMAGE");
            Put("bad.jpg", Jpeg(23, 11, 0xc3)); Reject(() => assets.ReadImageInfo("bad.jpg"), "unsupported JPEG frame", "ASSET_IMAGE");
            Put("bad.jpg", [.. jpeg[..21], .. jpeg[8..21], .. jpeg[21..]]);
            Reject(() => assets.ReadImageInfo("bad.jpg"), "duplicate JPEG frame", "ASSET_IMAGE");
            Put("padding.jpg", [0xff, 0xd8, 0xff, 0x01, 0xff, .. jpeg[2..]]);
            Check(assets.ReadImageInfo("padding.jpg").Height == 11, "JPEG fill and TEM markers safely skipped");
            // A metadata segment can span thousands of bytes without proportional allocations.
            using (var stream = new MemoryStream())
            {
                stream.Write([0xff, 0xd8, 0xff, 0xe1, 0xff, 0xff]); stream.Write(new byte[65533]); stream.Write(jpeg.AsSpan(2));
                Put("metadata.jpg", stream.ToArray());
            }
            Check(assets.ReadImageInfo("metadata.jpg").Width == 23, "bounded JPEG APP metadata skip");
            Put("topdown.bmp", Bmp(13, -7)); Check(assets.ReadImageInfo("topdown.bmp").Height == 7, "top-down BMP absolute height");
            modified = Bmp(13, 7); BinaryPrimitives.WriteUInt32LittleEndian(modified.AsSpan(14), uint.MaxValue);
            Put("bad.bmp", modified); Reject(() => assets.ReadImageInfo("bad.bmp"), "BMP DIB exceeds file", "ASSET_IMAGE");
            modified = Bmp(13, 7); BinaryPrimitives.WriteInt32LittleEndian(modified.AsSpan(14), 12);
            Put("bad.bmp", modified); Reject(() => assets.ReadImageInfo("bad.bmp"), "unsupported BMP DIB", "ASSET_IMAGE");
            foreach (var (path, bytes) in new[]
            {
                ("large.png", Png(4097, 1)), ("large.png", Png(uint.MaxValue, uint.MaxValue)),
                ("large.png", Png(0, 1)), ("large.bmp", Bmp(1, int.MinValue)),
                ("large.bmp", Bmp(-1, 1)), ("large.jpg", Jpeg(65535, 1)), ("large.jpg", Jpeg(1, 0))
            })
            { Put(path, bytes); Reject(() => assets.ReadImageInfo(path), "dimension/RGBA bounds " + path, "ASSET_IMAGE_SIZE"); }
            Put("limit.png", Png(4096, 4096));
            Check(ImageAsset.Read(assets, "limit.png").DecodedBytes == ImageAsset.MaximumDecodedBytes, "inclusive decoded RGBA limit");
            using (var stream = File.Create(assets.FilePath("limit.bmp"))) { stream.Write(Bmp(1, 1)); stream.SetLength(ImageAsset.MaximumEncodedBytes); }
            Check(assets.ReadImageInfo("limit.bmp").Width == 1 && ImageAsset.Read(assets, "limit.bmp").EncodedLength == ImageAsset.MaximumEncodedBytes, "inclusive encoded limit");
            using (var stream = File.OpenWrite(assets.FilePath("limit.bmp"))) stream.SetLength(ImageAsset.MaximumEncodedBytes + 1L);
            Reject(() => assets.ReadImageInfo("limit.bmp"), "oversized streaming preflight", "ASSET_IMAGE_SIZE");
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            Reject(() => ImageAsset.Read(assets, "limit.bmp"), "oversized snapshot rejected before allocation", "ASSET_IMAGE_SIZE");
            Check(GC.GetAllocatedBytesForCurrentThread() - allocated < 1024 * 1024, "oversized snapshot never allocates a file-sized buffer");
            Reject(() => assets.ReadBitmapInfo("limit.bmp"), "legacy oversize diagnostic", "ASSET_BMP");

            var snapshot = ImageAsset.Read(assets, "sample.png");
            Put("sample.png", [1, 2, 3]);
            Check(snapshot.Bytes.SequenceEqual(png) && snapshot.Info.Width == 17, "snapshot unaffected by source replacement");
            File.Delete(assets.FilePath("sample.png"));
            Check(snapshot.Bytes.SequenceEqual(png), "snapshot unaffected by source deletion");
            Put("sample.png", png);
            WorldLoadersAndLifetimes(assets, Check);
            Console.WriteLine($"IMAGE SELF-TEST PASS assertions={checks}; bounded metadata and headless lifetime checks, no pixel decoding");
            return checks;
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void WorldLoadersAndLifetimes(AssetRoot assets, Action<bool, string> check)
    {
        string sceneJson = """
            {"kind":"gal-authored-scene","version":1,"id":"00000000-0000-0000-0000-000000000001",
             "persistentScopeId":"00000000-0000-0000-0000-000000000002","name":"Images",
             "resources":[{"key":"png","path":"sample.png"},{"key":"jpeg","path":"sample.JPEG"}],"entities":[]}
            """;
        var scene = AuthoredScene.Load(sceneJson, assets.FilePath("images.scene.json"), assets);
        check(scene.Catalog.PathFor("png") == assets.FilePath("sample.png") && scene.Resources["jpeg"] == assets.FilePath("sample.JPEG"), "authored scene and catalog share image preflight");
        var source = new TileMapDocument
        {
            Kind = "gal-tilemap", Version = 1, Name = "Images", Width = 1, Height = 1, TileWidth = 1, TileHeight = 1,
            Resources = [new() { Key = "png", Path = "sample.png", Region = new() { X = 1, Y = 1, Width = 16, Height = 8 } }],
            Tiles = [new() { Id = 1, AssetKey = "png" }], Layers = [new() { Name = "images", Order = 0, Cells = [1] }]
        };
        var map = TileMapAsset.Build(source, assets.FilePath("images.tilemap.json"), assets);
        check(map.Catalog.TextureFor("png").Region == new TextureRegion(1, 1, 16, 8), "tilemap PNG dimensions bound atlas region");
        using var engine = new EngineHost(true, 64);
        var first = engine.Textures.Acquire(assets, "sample.png");
        var jpeg = engine.Textures.Acquire(assets, "sample.JPEG");
        File.Delete(assets.FilePath("sample.png"));
        File.WriteAllBytes(assets.FilePath("sample.JPEG"), [0]);
        using (var retainedPng = engine.Textures.Acquire(assets, "sample.png"))
        using (var retainedJpeg = engine.Textures.Acquire(assets, "sample.JPEG"))
            check(retainedPng.Info.Width == 17 && retainedJpeg.Info.Width == 23 && engine.Textures.Loads == 2, "PNG/JPEG leases retain source snapshot after delete/replacement");
        using (var bank = new TextureBank(engine, scene.Catalog))
        {
            var world = new World(); var worldScene = world.CreateScene("images");
            world.Create("png", worldScene).Sprite = new Sprite2D(1, 1, AssetKey: "png");
            world.Create("jpeg", worldScene).Sprite = new Sprite2D(1, 1, AssetKey: "jpeg");
            bank.Sync(world);
            check(bank.LoadedCount == 2 && engine.Textures.Loads == 2, "bank validation honors retained PNG/JPEG dimensions without reread");
        }
        first.Dispose(); jpeg.Dispose();
        check(engine.Textures.Count == 0 && engine.Textures.Loads == engine.Textures.Releases && engine.TextureCount == 0, "headless image leases balance without decode or native texture creation");
        try { engine.Textures.Acquire(assets, "sample.png"); throw new InvalidOperationException("Deleted PNG accepted after release"); }
        catch (AssetException e) { check(e.Code == "ASSET_MISSING", "final release observes deleted image on later load"); }
        try { engine.Textures.Acquire(assets, "sample.JPEG"); throw new InvalidOperationException("Replaced JPEG accepted after release"); }
        catch (AssetException e) { check(e.Code == "ASSET_IMAGE", "final release revalidates replaced JPEG"); }

        var large = new List<TextureLease>();
        try
        {
            for (int i = 0; i < 5; i++) File.WriteAllBytes(assets.FilePath($"budget-{i}.png"), Png(4096, 4096));
            for (int i = 0; i < 4; i++) large.Add(engine.Textures.Acquire(assets, $"budget-{i}.png"));
            check(engine.Textures.DecodedBytes == TextureCache.MaximumDecodedBytes && engine.Textures.Count == 4, "inclusive 256 MiB cache RGBA budget without pixel allocation");
            using (var alias = engine.Textures.Acquire(assets, "budget-0.png"))
                check(engine.Textures.DecodedBytes == TextureCache.MaximumDecodedBytes && engine.Textures.Count == 4, "retained alias consumes no extra decoded budget");
            try { engine.Textures.Acquire(assets, "budget-4.png"); throw new InvalidOperationException("Excess cache RGBA budget accepted"); }
            catch (AssetException e) { check(e.Code == "ASSET_CAPACITY" && engine.Textures.Count == 4, "fifth 64 MiB image rejected transactionally"); }
            var catalog = new AssetCatalog(assets, new Dictionary<string, string> { ["old"] = "budget-0.png", ["new"] = "budget-4.png" });
            using (var bank = new TextureBank(engine, catalog))
            {
                var world = new World(); var worldScene = world.CreateScene("budget");
                var entity = world.Create("image", worldScene); entity.Sprite = new Sprite2D(1, 1, AssetKey: "old");
                bank.Sync(world);
                entity.Sprite = new Sprite2D(1, 1, AssetKey: "new");
                try { bank.Sync(world); throw new InvalidOperationException("Bank over-budget replacement accepted"); }
                catch (AssetException e)
                {
                    check(e.Code == "ASSET_CAPACITY" && bank.LoadedCount == 1 && bank.Resolve("old") == 0
                        && engine.Textures.DecodedBytes == TextureCache.MaximumDecodedBytes,
                        "candidate budget includes old residency and retains previous usable bank");
                }
            }
            large[0].Dispose();
            check(engine.Textures.DecodedBytes == TextureCache.MaximumDecodedBytes - ImageAsset.MaximumDecodedBytes, "final release recovers decoded budget");
            large.Add(engine.Textures.Acquire(assets, "budget-4.png"));
            check(engine.Textures.Count == 4 && engine.Textures.DecodedBytes == TextureCache.MaximumDecodedBytes, "recovered decoded budget permits next image");
        }
        finally { foreach (var lease in large) lease.Dispose(); }
        check(engine.Textures.DecodedBytes == 0 && engine.Textures.Count == 0, "all decoded cache budget recovered");
    }

    private static byte[] Bmp(int width, int height)
    {
        byte[] bytes = new byte[54]; bytes[0] = (byte)'B'; bytes[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(18), width);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(22), height);
        return bytes;
    }

    private static byte[] Png(uint width, uint height)
    {
        using var stream = new MemoryStream(); stream.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        byte[] header = new byte[13]; BinaryPrimitives.WriteUInt32BigEndian(header, width); BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; header[9] = 6;
        Chunk(stream, "IHDR", header); Chunk(stream, "IDAT", [0]); Chunk(stream, "IEND", []);
        return stream.ToArray();
    }

    private static void Chunk(Stream stream, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(length, data.Length); stream.Write(length);
        foreach (char c in type) stream.WriteByte((byte)c);
        stream.Write(data); stream.Write([0, 0, 0, 0]); // Deliberately no CRC/decode assertion in this CPU fixture.
    }

    private static byte[] Jpeg(ushort width, ushort height, byte marker = 0xc0)
    {
        byte[] bytes = [0xff, 0xd8, 0xff, 0xe0, 0, 4, 0, 0,
            0xff, marker, 0, 11, 8, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, 1, 1, 0x11, 0,
            0xff, 0xda, 0, 8, 1, 1, 0, 0, 63, 0, 0xff, 0xd9];
        return bytes;
    }
}
