using System.Runtime.CompilerServices;
using GameAuthoringLab;

// Public package APIs only: no engine project references, internals or native probes.
internal static class DiagnosticChecks
{
    private sealed record Item(ulong Id, string Title, double Amount);
    private sealed record Section(Item[] Items);
    private sealed record Model(Section[] Sections);
    private static UiDeclaration NextLine([CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        => new(file, line + 1);

    public static void Run(EngineHost engine, string outputDirectory)
    {
        int checks = 0;
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException("DIAGNOSTIC CHECK: " + name);
            checks++;
        }
        string[] nativePaths = File.ReadLines("/proc/self/maps")
            .Where(line => line.EndsWith("/libgal.so", StringComparison.Ordinal))
            .Select(line => line[line.IndexOf('/')..]).Distinct().ToArray();
        string applicationRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory))
            + Path.DirectorySeparatorChar;
        Check(nativePaths.Length == 1 && Path.GetFullPath(nativePaths[0]).StartsWith(applicationRoot, StringComparison.Ordinal),
            "exactly one libgal.so is loaded from this independent JIT/AOT application's package output");
        File.WriteAllLines(Path.Combine(outputDirectory, "package-native-path.txt"), nativePaths);
        Console.WriteLine($"UI ERGONOMICS PACKAGE NATIVE PASS path={nativePaths[0]}");

        string directory = Path.Combine(outputDirectory, "diagnostic-assets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            const string source = """
                <rml>
                  <head><title>Diagnostic fixture</title><link type="text/rcss" href="fixture.rcss" /></head>
                  <body data-model="model">
                    <div data-for="section : state.sections">
                      <div data-for="item : section.items">
                        <button data-event-click="choose(item.id)">Choose</button>
                        <span>{{item.title}} {{item.amount}}</span>
                      </div>
                    </div>
                  </body>
                </rml>
                """;
            string rmlPath = Path.Combine(directory, "fixture.rml");
            File.WriteAllText(rmlPath, source);
            File.WriteAllText(Path.Combine(directory, "fixture.rcss"),
                "body { font-family: \"Noto Sans CJK SC\"; font-size: 14px; } div { display: block; }");
            var item = new UiRecord<Item>().Key("id", value => value.Id);
            UiDeclaration textOrigin = NextLine();
            item.Text("title", value => value.Title);
            UiDeclaration numberOrigin = NextLine();
            item.Number("amount", value => value.Amount);
            var section = new UiRecord<Section>().Array("items", value => value.Items, item, 2);
            var schema = new UiRecord<Model>().Array("sections", value => value.Sections, section, 2);
            UiDeclaration commandOrigin = NextLine();
            var commands = new UiCommands().On("choose", 1, UiArgs.Key, (ulong _) => { });
            using var session = new UiModelSession<Model>(engine, schema, commands);
            var assets = new AssetRoot(directory);
            void Draw() => engine.Draw(new Camera { Zoom = 1 }, ReadOnlySpan<SpriteDraw>.Empty);
            session.LoadAsset(assets, "fixture.rml");
            Draw();
            var emptyNested = new Model([new Section([])]);
            Check(session.Apply(emptyNested), "initial nested-empty model is accepted");
            Draw();
            UiBindingStatus live = session.Status;
            Check(live.Loaded && !live.Pending && live.Diagnostic.Length == 0 && live.Overflow == 0,
                "valid nested-empty loops load without diagnostics");

            void SourceError(string broken, string code, string field, int line, string cause, bool hasDeclaration)
            {
                File.WriteAllText(rmlPath, broken);
                try { session.LoadAsset(assets, "fixture.rml"); throw new InvalidOperationException("Broken RML accepted"); }
                catch (UiAuthoringException error)
                {
                    Check(error.Code == code && error.FilePath == rmlPath && error.Line == line && error.Column > 0,
                        code + " identifies the authored RML use location");
                    Check(error.Field == field && error.Cause.Contains(cause, StringComparison.Ordinal),
                        code + " identifies the exact field and actionable cause");
                    Check(hasDeclaration ? error.Declaration == commandOrigin && error.FilePath != commandOrigin.FilePath
                        : error.Declaration is null, code + " keeps use and declaration origins distinct");
                }
                UiBindingStatus retained = session.Status;
                Check(retained.Loaded && !retained.Pending && retained.Generation == live.Generation &&
                    retained.Revision == live.Revision && retained.Diagnostic.Length == 0,
                    "failed source reload retains the previously published document and revision");
                ulong applies = session.NativeApplyCalls;
                Check(!session.Apply(emptyNested) && session.NativeApplyCalls == applies,
                    "failed source reload retains the live applied snapshot");
                Draw();
            }
            SourceError(source.Replace("item.title", "item.missing", StringComparison.Ordinal), "UI_BINDING",
                "rml/body/div/div/span/text()", 7, "item.missing", false);
            SourceError(source.Replace("choose(item.id)", "choose(item.title)", StringComparison.Ordinal), "UI_COMMAND",
                "rml/body/div/div/button@data-event-click", 6, "expects Key", true);
            SourceError(source.Replace("choose(item.id)", "choose()", StringComparison.Ordinal), "UI_COMMAND",
                "rml/body/div/div/button@data-event-click", 6, "expects 1 argument(s)", true);
            File.WriteAllText(rmlPath, source);

            var valid = new Model([new Section([new Item(41, "First", 1), new Item(42, "Second", 2)])]);
            Check(session.Apply(valid), "populated nested model is accepted after rejected reloads");
            Draw();
            live = session.Status;
            void ValueError(Model broken, string field, UiDeclaration declaration, bool text)
            {
                ulong applies = session.NativeApplyCalls;
                try { session.Apply(broken); throw new InvalidOperationException("Invalid model value accepted"); }
                catch (UiAuthoringException error)
                {
                    Check(error.Code == "UI_MODEL_VALUE" && error.Field == field,
                        "runtime invalid value includes the complete nested index breadcrumb");
                    Check(error.Declaration == declaration && error.FilePath == declaration.FilePath &&
                        error.Line == declaration.Line && error.Column == 1 && error.FilePath != rmlPath,
                        "runtime invalid value names its real C# declaration without inventing a data-source location");
                    Check(error.Cause.Contains("runtime data source is unknown", StringComparison.Ordinal) &&
                        (text ? error.InnerException is UiAuthoringException { Code: "UI_MODEL", FilePath: "<model>", Field: "state" }
                            : error.InnerException is ArgumentOutOfRangeException),
                        "runtime diagnostic states origin limits and preserves the original exception");
                }
                Check(session.NativeApplyCalls == applies && session.Status.Revision == live.Revision &&
                    !session.Apply(valid), "invalid projection preserves the native snapshot and revision");
            }
            ValueError(new Model([new Section([new Item(41, "First", 1), new Item(42, new string('x', 256), 2)])]),
                "state.sections[0].items[1].title", textOrigin, true);
            ValueError(new Model([new Section([new Item(41, "First", 1), new Item(42, "Second", double.NaN)])]),
                "state.sections[0].items[1].amount", numberOrigin, false);
            Check(session.Status.Diagnostic.Length == 0 && session.Status.Overflow == 0,
                "source and runtime failures leave the live binding healthy");
        }
        finally { Directory.Delete(directory, recursive: true); }
        Check(!Directory.Exists(directory), "temporary diagnostic assets are cleaned up");
        Console.WriteLine($"UI ERGONOMICS DIAGNOSTICS PASS assertions={checks}");
    }
}
