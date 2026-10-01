using System.Text.Json.Nodes;

namespace GameAuthoringLab;

internal static class AuthoredSceneTests
{
    public static int Run()
    {
        int checks = 0;
        void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException("AUTHORED SCENE TEST: " + message); }
        string directory = Path.Combine(Path.GetTempPath(), "gal-authored-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string source = Path.Combine(AppContext.BaseDirectory, "assets", "compositions.scene.json");
            string file = Path.Combine(directory, "example.scene.json");
            File.Copy(Path.Combine(AppContext.BaseDirectory, "assets", "cell.bmp"), Path.Combine(directory, "cell.bmp"));
            File.Copy(source, file);
            var first = AuthoredScene.LoadFile(file); string original = AuthoredScene.Write(first.Source, file);
            Check(first.World.EntityCount == 4 && first.Resources.Count == 1, "flat two-instance fixture loads");
            Check(AuthoredScene.Write(AuthoredScene.Load(original, file).Source, file) == original, "canonical source round-trip is exact");
            var changed = JsonNode.Parse(original)!.AsObject();
            Guid secondId = first.Source.Entities[2].Id;
            changed["entities"]![2]!["transform"]!["x"] = 420;
            var edited = AuthoredScene.Load(changed.ToJsonString(), file);
            Check(edited.World.GetPersistent(secondId).LocalTransform.X == 420, "single instance position edit loads");
            Check(edited.Source.Entities.Select(e => e.Id).SequenceEqual(first.Source.Entities.Select(e => e.Id)), "all stable IDs/order retained");
            Check(edited.World.GetPersistent(first.Source.Entities[0].Id).LocalTransform == first.World.GetPersistent(first.Source.Entities[0].Id).LocalTransform, "other instance unchanged");
            Check(edited.World.GetPersistent(first.Source.Entities[3].Id).TransformParent?.PersistentId == secondId && edited.World.GetPersistent(first.Source.Entities[3].Id).LifetimeOwner?.PersistentId == secondId, "second instance internal references remain local");
            AuthoredScene.SaveFile(edited.Source, file);
            var reloaded = AuthoredScene.LoadFile(file);
            Check(reloaded.World.GetPersistent(secondId).LocalTransform.X == 420, "edited source saves and loads from file");
            Check(reloaded.World.GetPersistent(secondId).Id != edited.World.GetPersistent(secondId).Id, "persistent ID does not reuse runtime identity");
            Check(!File.ReadAllText(file).Contains("pickupCount", StringComparison.Ordinal), "authored source contains no game progress");
            var batch = new SpriteBatch { TextureResolver = _ => 0 };
            first.World.ExtractSprites(batch);
            Check(batch.Draws[0].X == 240 && batch.Draws[1].X == 360, "equal-layer authored array order retained");

            void Reject(Action<JsonObject> mutation, string code, string expectedPath, bool entity = false)
            {
                var root = JsonNode.Parse(original)!.AsObject(); mutation(root);
                try { _ = AuthoredScene.Load(root.ToJsonString(), file); throw new InvalidOperationException("accepted invalid source " + expectedPath); }
                catch (AuthoredSceneException e)
                {
                    Check(e.Code == code && e.FilePath == file && e.JsonPath == expectedPath && (!entity || e.EntityId is not null), "diagnostic " + expectedPath + " includes code/file/path/entity");
                    Check(first.World.EntityCount == 4, "failed candidate load leaves prior world intact");
                }
            }
            Reject(d => d["kind"] = "runtime-save", "SCENE_KIND", "$.kind");
            Reject(d => d["version"] = 3, "SCENE_VERSION", "$.version");
            Reject(d => d["extra"] = true, "SCENE_JSON", "$.extra");
            Reject(d => d["entities"]![0]!["note"] = "unknown", "SCENE_JSON", "$.entities[0].note");
            Reject(d => d["id"] = Guid.Empty.ToString(), "SCENE_ID", "$.id");
            Reject(d => d["entities"]![2]!["id"] = first.Source.Entities[0].Id.ToString(), "SCENE_ID", "$.entities[2].id", true);
            Reject(d => d["entities"]![0]!["sceneId"] = Guid.NewGuid().ToString(), "SCENE_REFERENCE", "$.entities[0].sceneId", true);
            Reject(d => d["entities"]![1]!["parentId"] = Guid.NewGuid().ToString(), "SCENE_REFERENCE", "$.entities[1].parentId", true);
            Reject(d => d["entities"]![1]!["ownerId"] = Guid.NewGuid().ToString(), "SCENE_REFERENCE", "$.entities[1].ownerId", true);
            Reject(d => d["entities"]![0]!["parentId"] = first.Source.Entities[1].Id.ToString(), "SCENE_GRAPH", "$.entities[0].parentId", true);
            Reject(d => d["entities"]![0]!["ownerId"] = first.Source.Entities[1].Id.ToString(), "SCENE_GRAPH", "$.entities[0].ownerId", true);
            Reject(d => d["entities"]![0]!["transform"]!["scaleX"] = 0, "SCENE_TRANSFORM", "$.entities[0].transform", true);
            Reject(d => d["entities"]![0]!["sprite"]!["a"] = 2, "SCENE_SPRITE", "$.entities[0].sprite", true);
            Reject(d => d["entities"]![0]!["sprite"]!["assetKey"] = "no-such-key", "SCENE_RESOURCE", "$.entities[0].sprite.assetKey", true);
            Reject(d => d["resources"]![0]!["path"] = "missing.bmp", "SCENE_RESOURCE", "$.resources[0].path");
            Reject(d => d["resources"]![0]!["path"] = "../cell.bmp", "SCENE_RESOURCE", "$.resources[0].path");
            Reject(d => d["resources"]![0]!["path"] = "/tmp/cell.bmp", "SCENE_RESOURCE", "$.resources[0].path");
            Reject(d => d["resources"]!.AsArray().Add(d["resources"]![0]!.DeepClone()), "SCENE_RESOURCE", "$.resources[1].key");
            Reject(d => {d["entities"]![0]!["transform"]!["scaleX"] = 1e30f;d["entities"]![1]!["transform"]!["scaleX"] = 1e30f;}, "SCENE_TRANSFORM", "$.entities[1].transform", true);
            Reject(d => d["entities"]![0] = null, "SCENE_ENTITY", "$.entities[0]");
            Reject(d => d["resources"]![0] = null, "SCENE_RESOURCE", "$.resources[0]");
            Reject(d => d.Remove("resources"), "SCENE_JSON", "$");
            var reversed = JsonNode.Parse(original)!.AsObject();
            var reversedRows = reversed["entities"]!.AsArray().Select(e => e!.DeepClone()).Reverse().ToArray();
            reversed["entities"] = new JsonArray(reversedRows);
            var reordered = AuthoredScene.Load(reversed.ToJsonString(), file); reordered.World.ExtractSprites(batch);
            Check(batch.Draws[0].X == 360 && batch.Draws[1].X == 240, "explicit array reorder changes equal-layer order without GUID sorting");
            var chain = JsonNode.Parse(original)!.AsObject(); var rows = chain["entities"]!.AsArray();
            var template = rows[0]!.DeepClone(); rows.Clear();
            Guid ChainId(int n) => Guid.Parse($"00000000-0000-4001-8000-{n:000000000000}");
            for (int i = 0; i < 513; i++)
            {
                var row = template.DeepClone(); row["id"] = ChainId(i + 1).ToString(); row["parentId"] = i == 0 ? null : ChainId(i).ToString();
                row["sprite"] = null; rows.Add(row);
            }
            var extra = rows[512]!.DeepClone(); rows.RemoveAt(512);
            Check(AuthoredScene.Load(chain.ToJsonString(), file).World.EntityCount == 512, "512-level relation accepted"); rows.Add(extra);
            try { AuthoredScene.Load(chain.ToJsonString(), file); throw new InvalidOperationException("accepted 513-level chain"); }
            catch (AuthoredSceneException e) { Check(e.Code == "SCENE_GRAPH" && e.JsonPath == "$.entities[512].parentId", "depth overflow has precise entity path"); }
            try { AuthoredScene.Load(original[..^1] + ",\"version\":1}", file); throw new InvalidOperationException("accepted duplicate field"); }
            catch (AuthoredSceneException e) { Check(e.Code == "SCENE_JSON", "duplicate JSON properties rejected"); }
            try { AuthoredScene.Load(ScenePersistence.Save(first.World), file); throw new InvalidOperationException("accepted runtime save as authored scene"); }
            catch (AuthoredSceneException e) { Check(e.Code == "SCENE_JSON", "runtime save rejected as source"); }
            // Validation failures must not replace an existing valid source file.
            string before = File.ReadAllText(file); edited.Source.Resources[0] = new() { Key = "cell", Path = "missing.bmp" };
            try { AuthoredScene.SaveFile(edited.Source, file); throw new InvalidOperationException("saved missing resource"); }
            catch (AuthoredSceneException) { Check(File.ReadAllText(file) == before, "invalid save preserves prior file bytes"); }
            edited.Source.Resources[0] = new() { Key = "cell", Path = "cell.bmp" };
            string destinationDirectory = Path.Combine(directory, "cannot-replace.scene.json"); Directory.CreateDirectory(destinationDirectory);
            try { AuthoredScene.SaveFile(edited.Source, destinationDirectory); throw new InvalidOperationException("replaced directory"); }
            catch (AuthoredSceneException e) { Check(e.Code == "SCENE_WRITE" && Directory.Exists(destinationDirectory), "failed file replacement preserves destination"); }
            Check(!Directory.EnumerateFiles(directory, "*.tmp").Any(), "failed write removes temporary file");
            File.WriteAllBytes(Path.Combine(directory, "cell.bmp"), new byte[54]);
            try { AuthoredScene.LoadFile(file); throw new InvalidOperationException("accepted bad BMP"); }
            catch (AuthoredSceneException e) { Check(e.Code == "SCENE_RESOURCE" && e.JsonPath == "$.resources[0].path", "invalid BMP header diagnosed"); }
            Check(Program.Options.TryParse(["--authored-demo", file, "--headless"], out var options, out _) && options.AuthoredPath == file && options.Frames == 120, "authored CLI headless default");
            Check(!Program.Options.TryParse(["--validate-scene"], out _, out _), "authored validation requires path");
            Console.WriteLine($"PASS authored scene source/load/edit/save, stable identities/order/resources, diagnostics and failed-write retention ({checks} checks)");
            return checks;
        }
        finally { Directory.Delete(directory, true); }
    }
}
