using System.Runtime.CompilerServices;
using System.Text;
namespace GameAuthoringLab;
internal static class UiErgonomicsTests
{
    private sealed class Item { public ulong Id = ulong.MaxValue; public string Title = "one"; public double Number = 2; }
    private sealed class Group { public List<Item> Items = [new()]; }
    private sealed class Model { public string Title = "root"; public List<Group> Groups = [new()]; }
    private const string Rml = """
        <rml><head><title>Typed diagnostics</title><link type="text/rcss" href="typed.rcss"/></head>
        <body data-model="model"><div data-for="group, gi : state.groups"><div data-for="item, ii : group.items">
        <button id="choose" data-event-click="choose(item.id)">{{item.title}}</button>
        </div></div><input id="input" type="text" data-attr-value="state.title" data-event-change="edit(ev.value)"/>
        <button id="act" data-event-click="act">Act</button></body></rml>
        """;
    private const string Css = "body { font-family: Noto Sans CJK SC; font-size: 18px; } button { width: 200px; height: 40px; } input { width: 300px; height: 40px; }";
    private static UiRecord<Model> Schema() => new UiRecord<Model>().Text("title", m => m.Title)
        .Array("groups", m => m.Groups, new UiRecord<Group>().Array("items", g => g.Items,
            new UiRecord<Item>().Key("id", i => i.Id).Text("title", i => i.Title).Number("number", i => i.Number)));
    private static UiCommands Commands() => new UiCommands().On("choose", 1, UiArgs.Key, _ => { })
        .On("edit", 2, UiArgs.Text, _ => { }).On("act", 3, () => { });
    private static UiAuthoringException Error(Action action)
    {
        try { action(); } catch (UiAuthoringException e) { return e; }
        throw new Exception("Expected origin-aware UI error");
    }
    public static int RunContracts()
    {
        int count = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception("UI ERGONOMICS: " + message); count++; }
        var schema = new List<ModelSchema>(); var projection = Schema().Compile("state", uint.MaxValue, schema, 1);
        var commands = Commands().Freeze();
        void Validate(string source, UiCommands.Command[]? definitions = null) => UiModelAuthoring.Validate(Encoding.UTF8.GetBytes(source), Encoding.UTF8.GetBytes(Css),
            (definitions ?? commands).Select(c => c.Name).ToArray(), "typed.rml", "typed.rcss", schema: schema.ToArray(), definitions: definitions ?? commands);
        Validate(Rml); Check(true, "nested aliases and empty-independent schema validation");
        var wrongField = Error(() => Validate(Rml.Replace("item.title", "item.typo")));
        Check(wrongField.FilePath == "typed.rml" && wrongField.Line == 3 && wrongField.Column > 0 && wrongField.Cause.Contains("typo"), "RML interpolation origin");
        var wrongKey = Error(() => Validate(Rml.Replace("choose(item.id)", "choose(item.title)")));
        Check(wrongKey.Code == "UI_COMMAND" && wrongKey.FilePath == "typed.rml" && wrongKey.Line == 3 && wrongKey.Declaration?.FilePath.EndsWith("UiErgonomicsTests.cs") == true, "key argument separates use and declaration");
        var arity = Error(() => Validate(Rml.Replace("choose(item.id)", "choose(item.id, item.title)")));
        Check(arity.Code == "UI_COMMAND" && arity.Line == 3, "command arity at authored invocation");
        Check(Error(() => Validate(Rml.Replace("choose(item.id)", "choose"))).Code == "UI_COMMAND", "bare argument command arity");
        var nested = new Model(); nested.Groups.Clear(); var writer = new UiModelWriter(); projection(nested, writer);
        Check(writer.Count == 3, "empty groups project");
        var emptyLoop = Error(() => Validate(Rml.Replace("group.items", "group.missing")));
        Check(emptyLoop.Line == 2 && emptyLoop.Cause.Contains("missing"), "empty loop resolves element schema");
        Validate(Rml.Replace("edit(ev.value)", "edit(ev.value | to_lower)")); Check(true, "unproved expression syntax remains native-owned");
        Validate(Rml.Replace("<body data-model=\"model\">", "<body data-model=\"model\"><div data-testid='inventory-card' data-custom='state.not_a_binding' data-visiblehint='not an expression'/><p data-text='ignored attribute metadata'>{{state.title}}</p>"));
        Check(true, "unknown views and data-text metadata are not model expressions");
        Validate(Rml.Replace("</body>", "<textarea>{{not_a_model_variable}}</textarea></body>"));
        Check(true, "textarea authored value retains literal braces");
        Validate(Rml.Replace("</body>", "<p>&#123;&#123;not_a_model_variable&#125;&#125;</p></body>"));
        Check(true, "escaped-only braces stay literal before native entity decoding");
        Check(Error(() => Validate(Rml.Replace("</body>", "<p>{{state.title}} &#123;&#123;state.missing&#125;&#125;</p></body>"))).Code == "UI_BINDING", "mixed raw and escaped expressions share a native text view");
        var legacy = new UiCommands().Add("choose", 1, UiValueKind.Key).Add("edit", 2, UiValueKind.Text).Add("act", 3).Freeze();
        Validate(Rml.Replace("choose(item.id)", "choose(state.title)"), legacy); Check(true, "legacy canonical text keys keep runtime validation");
        Check(Error(() => Validate(Rml.Replace("choose(item.id)", "choose(item.number)"), legacy)).Code == "UI_COMMAND", "legacy provably numeric key rejected");
        foreach (string view in new[] { "data-attr-title", "data-attrif-disabled", "data-class-active", "data-if", "data-visible", "data-style-color" })
            Check(Error(() => Validate(Rml.Replace("<button id=\"act\"", "<button " + view + "='state.typo' id=\"act\""))).Code == "UI_BINDING", "known view schema validation " + view);
        Validate(Rml.Replace("<button id=\"act\"", "<button data-alias-title='state.title' data-attr-title='title' id=\"act\"")); Check(true, "data alias resolves schema source");
        Check(Error(() => Validate(Rml.Replace("<button id=\"act\"", "<button data-alias-title='state.typo' id=\"act\""))).Code == "UI_BINDING", "bad data alias source");
        Validate(Rml.Replace("<button id=\"act\"", "<div data-alias-a='state.groups[0]'><p data-alias-a='state.title' data-alias-b='a.items'>{{b}}</p></div><button id=\"act\""));
        Check(true, "local alias inference does not impose XML attribute initialization order");
        nested = new Model(); nested.Groups[0].Items[0].Title = "bad\ud800";
        var badValue = Error(() => { writer.Reset(); projection(nested, writer); });
        Check(badValue.Code == "UI_MODEL_VALUE" && badValue.Field == "state.groups[0].items[0].title" && badValue.Declaration?.Line > 0 && badValue.FilePath.EndsWith("UiErgonomicsTests.cs") && badValue.InnerException is UiAuthoringException, "nested field/index projection breadcrumb at registration");
        nested.Groups[0].Items[0].Title = "valid"; nested.Groups[0].Items[0].Number = double.NaN;
        var badNumber = Error(() => { writer.Reset(); projection(nested, writer); });
        Check(badNumber.Field == "state.groups[0].items[0].number" && badNumber.InnerException is ArgumentOutOfRangeException, "nested number breadcrumb");
        nested.Groups[0].Items[0].Number = 1; writer.Reset(); projection(nested, writer); Check(writer.Count == 9, "projection recovers after failure");
        var duplicate = Error(() => new UiRecord<Model>().Text("title", m => m.Title).Text("title", m => m.Title));
        Check(duplicate.Code == "UI_SCHEMA" && duplicate.Declaration?.FilePath.EndsWith("UiErgonomicsTests.cs") == true, "schema declaration origin");
        var duplicateCommand = Error(() => new UiCommands().On("same", 1, () => { }).On("same", 2, () => { }));
        Check(duplicateCommand.Code == "UI_SCHEMA" && duplicateCommand.Declaration?.Line > 0, "command declaration origin");
        var badArray = Error(() => new UiRecord<Model>().Array("groups", m => m.Groups, new UiRecord<Group>().Text("name", _ => "group"), 65));
        Check(badArray.Code == "UI_SCHEMA" && badArray.Declaration?.Line > 0 && badArray.Field == "groups", "array limit declaration origin");
        var getterProjection = new UiRecord<Model>().Text("title", _ => throw new KeyNotFoundException("Missing application lookup")).Compile("state", uint.MaxValue, [], 1);
        var getterError = Error(() => { writer.Reset(); getterProjection(nested, writer); });
        Check(getterError.Field == "state.title" && getterError.InnerException is KeyNotFoundException, "getter lookup breadcrumb preserves cause");
        string result = "";
        var all = new UiCommands().On("zero", 1, () => result += "0").On("one", 2, UiArgs.Key, k => result += k)
            .On("two", 3, UiArgs.Text, UiArgs.Boolean, (t, b) => result += t + b)
            .On("three", 4, UiArgs.Number, UiArgs.Boolean, UiArgs.Text, (n, b, t) => result += n + t + b)
            .On("four", 5, UiArgs.Key, UiArgs.Text, UiArgs.Boolean, UiArgs.Number, (k, t, b, n) => result += k + t + b + n);
        var frozen = all.Freeze(); all.On("later", 6, () => { });
        Check(frozen.Length == 5 && frozen.Select(c => c.Arguments.Length).SequenceEqual(new[] { 0, 1, 2, 3, 4 }), "one definition freezes all native arities");
        UiCommandArgument key = new(UiValueKind.Key, "", 0, ulong.MaxValue), text = new(UiValueKind.Text, "text", 0, 0), boolean = new(UiValueKind.Boolean, "", 1, 0), number = new(UiValueKind.Number, "", 2, 0);
        frozen[4].Handler!(new(1, 1, 5, 4, key, text, boolean, number));
        Check(result == ulong.MaxValue + "textTrue2", "typed decoding retains exact ulong and argument order");
        var automatic = new UiCommands().On("z", () => { }).On("a", UiArgs.Key, _ => { })
            .Add("explicit_one", 1).On("explicit_max", uint.MaxValue, () => { }).On("middle", UiArgs.Text, UiArgs.Boolean, (_, _) => { });
        var allocated = automatic.Freeze();
        Check(allocated.Select(c => c.Id).Distinct().Count() == 5 && allocated.All(c => c.Id != 0), "automatic IDs avoid all explicit IDs");
        Check(allocated.Single(c => c.Name == "a").Id == 2 && allocated.Single(c => c.Name == "middle").Id == 3 && allocated.Single(c => c.Name == "z").Id == 4, "automatic IDs use ordinal names and reserve later explicit declarations");
        Check(allocated.Single(c => c.Name == "explicit_max").Id == uint.MaxValue, "explicit maximum ID preserved");
        Check(automatic.Freeze().Select(c => c.Id).SequenceEqual(allocated.Select(c => c.Id)), "repeated freeze stable and nonmutating");
        automatic.On("new", UiArgs.Number, UiArgs.Boolean, UiArgs.Text, (_, _, _) => { });
        Check(allocated.Length == 5 && automatic.Freeze().Length == 6, "automatic command freeze retains snapshot isolation");
        var reordered = new UiCommands().On("middle", UiArgs.Text, UiArgs.Boolean, (_, _) => { }).On("a", UiArgs.Key, _ => { })
            .On("explicit_max", uint.MaxValue, () => { }).On("z", () => { }).Add("explicit_one", 1).Freeze();
        Check(reordered.All(c => allocated.Single(a => a.Name == c.Name).Id == c.Id), "automatic allocation independent of insertion order");
        Check(Error(() => new UiCommands().On("same", () => { }).On("same", 1, () => { })).Code == "UI_SCHEMA", "mixed duplicate name rejected");
        Check(Error(() => new UiCommands().On("one", 3, () => { }).On("two", 3, () => { })).Code == "UI_SCHEMA", "explicit duplicate ID still rejected");
        Check(Error(() => new UiCommands().On("zero", 0, () => { })).Code == "UI_SCHEMA", "explicit zero remains invalid");
        var capacity = new UiCommands(); for (int i = 0; i < 32; i++) capacity.On("action_" + i, () => { });
        Check(capacity.Freeze().Select(c => c.Id).Distinct().Count() == 32, "all 32 automatic IDs distinct");
        Check(Error(() => capacity.On("overflow", () => { })).Code == "UI_SCHEMA", "automatic count still bounded");
        string autoResult = "";
        var autoFour = new UiCommands().On("four", UiArgs.Key, UiArgs.Text, UiArgs.Boolean, UiArgs.Number, (k, t, b, n) => autoResult = k + t + b + n).Freeze()[0];
        autoFour.Handler!(new(1, 1, autoFour.Id, 4, key, text, boolean, number));
        Check(autoResult == result, "automatic four-argument typed decoding unchanged");
        Console.WriteLine($"UI ERGONOMICS CONTRACT PASS assertions={count}"); return count;
    }
    public static int RunNative(EngineHost engine)
    {
        int count = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception("UI TYPED NATIVE: " + message); count++; }
        void Render() => engine.Draw(new Camera { Zoom = 1 }, ReadOnlySpan<Sprite>.Empty);
        string root = Path.Combine(Path.GetTempPath(), "gal-ui-typed-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "typed.rml"), Rml); File.WriteAllText(Path.Combine(root, "typed.rcss"), Css);
            var assets = new AssetRoot(root); var model = new Model(); ulong selected = 0; bool publishInsideHandler = false;
            UiModelSession<Model>? owner = null;
            using (var ui = owner = new UiModelSession<Model>(engine, Schema(), new UiCommands()
                .On("choose", UiArgs.Key, key => selected = key)
                .On("edit", UiArgs.Text, title => { model.Title = title; if (publishInsideHandler) owner!.Apply(model); })
                .On("act", () => model.Title = "acted")))
            {
                ui.LoadAsset(assets, "typed.rml"); Render(); ui.Apply(model); Render();
                ui.Probe(2, "choose"); var packet = ui.Poll();
                Check(ui.Dispatch(packet) && selected == ulong.MaxValue, "native event dispatches typed exact key");
                Check(!ui.Dispatch(packet with { Count = 0 }) && !ui.Dispatch(packet with { Argument0 = new(UiValueKind.Key, "", 0, 123) }), "dispatch checks signature and membership");
                ui.Probe(6, "input", value: "first"); ui.Probe(6, "input", value: "second"); var first = ui.Poll(); var second = ui.Poll();
                Check(ui.Dispatch(first) && ui.Dispatch(second) && model.Title == "second", "explicit drain dispatches both same-revision edits");
                ui.Apply(model); Render(); Check(!ui.Dispatch(first), "prior revision rejects typed handler");
                ui.Probe(6, "input", value: "third"); ui.Probe(6, "input", value: "fourth"); first = ui.Poll(); second = ui.Poll(); publishInsideHandler = true;
                Check(ui.Dispatch(first) && !ui.Dispatch(second) && model.Title == "third", "each dispatch rechecks immediately after earlier handler Apply");
                publishInsideHandler = false; Render();
                ui.Probe(1, "act"); first = ui.Poll(); ui.LoadAsset(assets, "typed.rml"); Render(); ui.Apply(model); Render(); Check(!ui.Dispatch(first), "generation reload rejects already-polled typed packet");
                uint revision = ui.Revision; ui.Probe(1, "choose"); model.Groups[0].Items[0].Title = "bad\ud800";
                var error = Error(() => ui.Apply(model));
                Check(error.Field == "state.groups[0].items[0].title" && ui.Revision == revision && !ui.Poll().IsEmpty, "nested projection failure preserves revision and queued events");
                model.Groups[0].Items[0].Title = "valid"; File.WriteAllText(Path.Combine(root, "typed.rml"), Rml.Replace("item.title", "item.missing"));
                var failed = Error(() => ui.LoadAsset(assets, "typed.rml")); Check(failed.Line == 3 && ui.Revision == revision && ui.Status.Loaded, "preflight failed reload retains live document");
            }
            File.WriteAllText(Path.Combine(root, "typed.rml"), Rml.Replace("choose(item.id)", "choose(state.title)").Replace("<body data-model=\"model\">", "<body data-model=\"model\"><div data-testid='inventory-card'/>").Replace("</body>", "<textarea>{{not_a_model_variable}}</textarea><p>&#123;&#123;not_a_model_variable&#125;&#125;</p><p>{{state.title}} &#123;&#123;state.title&#125;&#125;</p></body>"));
            using (var legacy = new UiModelSession<Model>(engine, Schema(), new UiCommands().Add("choose", 1, UiValueKind.Key).Add("edit", 2, UiValueKind.Text).Add("act", 3)))
            {
                model.Title = ulong.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture); legacy.LoadAsset(assets, "typed.rml"); Render(); legacy.Apply(model); Render(); legacy.Probe(2, "choose");
                Check(legacy.Poll()[0].Key == ulong.MaxValue, "legacy direct canonical text key and arbitrary metadata still load natively");
            }
            var (retained, capture) = CapturedSession(engine); retained.Dispose(); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Check(!capture.IsAlive, "disposed retained session releases captured typed handler"); GC.KeepAlive(retained);
        }
        finally { Directory.Delete(root, true); }
        Console.WriteLine($"UI TYPED NATIVE PASS assertions={count}"); return count;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (UiModelSession<Model>, WeakReference) CapturedSession(EngineHost engine)
    {
        var capture = new Model(); var commands = new UiCommands().On("capture", () => capture.Title = "used");
        return (new(engine, Schema(), commands), new(capture));
    }
}
