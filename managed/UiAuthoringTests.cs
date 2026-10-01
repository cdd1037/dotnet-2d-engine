using System.Text;

namespace GameAuthoringLab;

/// <summary>Pure managed tests: no native library, renderer, font, network, or external test package.</summary>
internal static class UiAuthoringTests
{
    private const string ValidRml = """
        <?xml version="1.0" encoding="utf-8"?>
        <rml>
          <head><title>Settings / 设置</title><link type="text/rcss" href="settings.rcss" /></head>
          <body><div id="settings-panel">
            <h1 id="panel-title">Settings / 设置</h1>
            <label id="name-label" for="player-name">Name / 名称</label>
            <input id="player-name" type="text" maxlength="32" value="Player / 玩家" />
            <label id="volume-label" for="volume">Volume / 音量</label>
            <input id="volume" type="range" min="0" max="100" step="1" value="65" />
            <div class="actions"><button id="apply">Apply / 应用</button><button id="reset">Reset / 重置</button></div>
            <p id="status">Ready / 就绪</p>
            <h2 id="list-title">Items / 项目</h2>
            <div id="item-list"><div class="list-item">One / 一</div><div class="list-item">Two / 二</div></div>
          </div></body>
        </rml>
        """;
    private const string ValidRcss = """
        /* Deliberately bounded settings profile, not general CSS. */
        body { font-family: "Noto Sans CJK SC"; font-size: 16dp; color: #ffffff; }
        #settings-panel { width: 470dp; padding: 18dp; }
        button { border-radius: 6dp; padding: 6dp 18dp; }
        button:hover { background-color: #4476a9; }
        #item-list { height: 104dp; overflow-x: hidden; overflow-y: auto; }
        .list-item { word-break: break-all; }
        #volume slidertrack { height: 8dp; }
        #item-list scrollbarvertical sliderbar { width: 10dp; }
        """;

    public static void Run(Action<bool, string> check)
    {
        ArgumentNullException.ThrowIfNull(check);
        UiValidatedDocument valid = Parse(ValidRml, ValidRcss);
        check(valid.PlayerName == UiSettingsContract.DefaultPlayerName && valid.Volume == UiSettingsContract.DefaultVolume,
            "UI authored initial model and native reset defaults agree");
        check(valid.Status == "Ready / 就绪" && valid.Items.SequenceEqual(new[] { "One / 一", "Two / 二" }), "UI multilingual plain-text model extracted");
        check(valid.Rml == ValidRml && valid.Rcss == ValidRcss, "UI validation preserves exact source snapshots");
        check(UiSettingsContract.Actions.Length == 4 && UiSettingsContract.Actions[0].Event == "change" &&
            UiSettingsContract.Actions[0].Action == "changed" && UiSettingsContract.Actions[1].Action == "changed" &&
            UiSettingsContract.Actions[2].ElementId == "apply" && UiSettingsContract.Actions[2].ValueKind == UiActionValueKind.None,
            "UI explicit by-ID listener contract");
        check(Parse("\uFEFF" + ValidRml, "\uFEFF" + ValidRcss).Volume == 65, "UI optional UTF-8 BOM accepted");
        check(Parse(ValidRml.Replace("Player / 玩家", "A &amp; B &lt; C", StringComparison.Ordinal), ValidRcss).PlayerName == "A & B < C",
            "UI XML built-in escapes decoded as plain text");
        check(Parse(ValidRml.Replace("value=\"65\"", "value=\"0\"", StringComparison.Ordinal), ValidRcss).Volume == 0, "UI zero volume accepted");
        check(Parse(ValidRml.Replace("value=\"65\"", "value=\"100\"", StringComparison.Ordinal), ValidRcss).Volume == 100, "UI maximum volume accepted");

        Reject(() => UiAuthoring.Validate([], Bytes(ValidRcss)), "UI_SIZE", "empty RML rejected");
        Reject(() => UiAuthoring.Validate(Bytes(ValidRml), []), "UI_SIZE", "empty RCSS rejected");
        Reject(() => UiAuthoring.Validate(new byte[UiAuthoring.MaxFileBytes + 1], Bytes(ValidRcss)), "UI_SIZE", "bounded RML bytes");
        Reject(() => UiAuthoring.Validate(Bytes(ValidRml), new byte[UiAuthoring.MaxFileBytes + 1]), "UI_SIZE", "bounded RCSS bytes");
        Reject(() => UiAuthoring.Validate([0xc3, 0x28], Bytes(ValidRcss)), "UI_UTF8", "invalid UTF-8 rejected");
        Reject(() => UiAuthoring.Validate(Bytes(ValidRml), [0xed, 0xa0, 0x80]), "UI_UTF8", "UTF-8 surrogate rejected");
        RejectRml(ValidRml.Replace("</rml>", "</broken>", StringComparison.Ordinal), "UI_XML", "malformed XML rejected");
        RejectRml("<!DOCTYPE rml [<!ENTITY x SYSTEM 'file:///etc/passwd'>]>" + WithoutDeclaration(), "UI_XML", "DTD and external entity rejected");
        RejectRml("<!DOCTYPE rml SYSTEM 'https://example.invalid/ui.dtd'>" + WithoutDeclaration(), "UI_XML", "remote DTD rejected without resolving");
        RejectRml(ValidRml.Replace("utf-8", "utf-16", StringComparison.Ordinal), "UI_XML_PROFILE", "non-UTF-8 XML declaration rejected");
        RejectRml(ValidRml.Replace("<body>", "<?include remote?><body>", StringComparison.Ordinal), "UI_XML_PROFILE", "processing instruction rejected");
        RejectRml(ValidRml.Replace("Ready / 就绪", "<![CDATA[Ready]]>", StringComparison.Ordinal), "UI_XML_PROFILE", "CDATA rejected");
        RejectRml(ValidRml.Replace("<rml>", "<rml xmlns=\"urn:unknown\">", StringComparison.Ordinal), "UI_STRUCTURE", "namespaced root rejected");
        RejectRml(ValidRml.Replace("<body>", "<body xmlns:x=\"urn:unknown\">", StringComparison.Ordinal), "UI_ATTRIBUTE", "namespace declaration rejected");
        RejectRml(ValidRml.Replace("<h1 id", "<typo id", StringComparison.Ordinal).Replace("</h1>", "</typo>", StringComparison.Ordinal), "UI_TAG", "unknown tag rejected");
        RejectRml(ValidRml.Replace("<body>", "<body surprise=\"true\">", StringComparison.Ordinal), "UI_ATTRIBUTE", "unknown attribute rejected");
        RejectRml(ValidRml.Replace("id=\"apply\"", "id=\"apply\" onclick=\"save()\"", StringComparison.Ordinal), "UI_BINDING", "inline action rejected");
        RejectRml(ValidRml.Replace("id=\"apply\"", "id=\"apply\" OnClick=\"save()\"", StringComparison.Ordinal), "UI_BINDING", "mixed-case inline action rejected");
        RejectRml(ValidRml.Replace("id=\"status\"", "id=\"status\" data-unknown=\"x\"", StringComparison.Ordinal), "UI_BINDING", "unknown data binding rejected");
        RejectRml(ValidRml.Replace("id=\"status\"", "id=\"status\" data-model=\"settings\"", StringComparison.Ordinal), "UI_BINDING", "implicit model bindings rejected");
        RejectRml(ValidRml.Replace("Ready / 就绪", "{{missing}}", StringComparison.Ordinal), "UI_BINDING", "text interpolation rejected");
        RejectRml(ValidRml.Replace("Player / 玩家", "{{name}}", StringComparison.Ordinal), "UI_BINDING", "attribute interpolation rejected");
        RejectRml(ValidRml.Replace("id=\"status\"", "id=\"status\" style=\"color:red\"", StringComparison.Ordinal), "UI_ATTRIBUTE", "inline style rejected");
        RejectRml(ValidRml.Replace("settings.rcss", "https://example.invalid/settings.rcss", StringComparison.Ordinal), "UI_RESOURCE", "remote stylesheet rejected");
        RejectRml(ValidRml.Replace("settings.rcss", "../settings.rcss", StringComparison.Ordinal), "UI_RESOURCE", "relative path traversal rejected");
        RejectRml(ValidRml.Replace("settings.rcss", "file:///tmp/settings.rcss", StringComparison.Ordinal), "UI_RESOURCE", "absolute stylesheet rejected");
        RejectRml(ValidRml.Replace("settings.rcss", "settings.rcss?x=1", StringComparison.Ordinal), "UI_RESOURCE", "stylesheet query rejected");
        RejectRml(ValidRml.Replace("id=\"status\"", "id=\"status\" src=\"https://example.invalid/x\"", StringComparison.Ordinal), "UI_ATTRIBUTE", "resource source attribute rejected");
        RejectRml(ValidRml.Replace("id=\"status\"", "id=\"stats\"", StringComparison.Ordinal), "UI_ID", "unknown ID rejected");
        RejectRml(ValidRml.Replace("id=\"reset\"", "id=\"apply\"", StringComparison.Ordinal), "UI_ID", "duplicate ID rejected");
        RejectRml(ValidRml.Replace("<p id=\"status\">Ready / 就绪</p>", "", StringComparison.Ordinal), "UI_REQUIRED", "missing required ID rejected");
        RejectRml(ValidRml.Replace("<button id=\"apply\">", "<button>", StringComparison.Ordinal), "UI_ID", "unregistered interactive element rejected");
        RejectRml(ValidRml.Replace("class=\"actions\"", "class=\"actons\"", StringComparison.Ordinal), "UI_CLASS", "unknown class rejected");
        RejectRml(ValidRml.Replace("for=\"player-name\"", "for=\"volume\"", StringComparison.Ordinal), "UI_VALUE", "misdirected label rejected");
        RejectRml(ValidRml.Replace("type=\"text\"", "type=\"password\"", StringComparison.Ordinal), "UI_VALUE", "unsupported input type rejected");
        RejectRml(ValidRml.Replace("maxlength=\"32\"", "maxlength=\"100000\"", StringComparison.Ordinal), "UI_VALUE", "name input length contract enforced");
        RejectRml(ValidRml.Replace("step=\"1\"", "step=\"0.1\"", StringComparison.Ordinal), "UI_VALUE", "range integer step required");
        RejectRml(ValidRml.Replace("value=\"65\"", "value=\"101\"", StringComparison.Ordinal), "UI_VALUE", "range maximum enforced");
        RejectRml(ValidRml.Replace("value=\"65\"", "value=\"NaN\"", StringComparison.Ordinal), "UI_VALUE", "non-numeric range rejected");
        RejectRml(ValidRml.Replace("value=\"65\"", "value=\"+1\"", StringComparison.Ordinal), "UI_VALUE", "noncanonical numeric sign rejected");
        RejectRml(ValidRml.Replace("Player / 玩家", new string('a', 33), StringComparison.Ordinal), "UI_VALUE", "authored name scalar bound");
        RejectRml(ValidRml.Replace("Ready / 就绪", new string('a', 256), StringComparison.Ordinal), "UI_VALUE", "authored status payload reserves NUL");
        RejectRml(ValidRml.Replace("One / 一", "<p>Nested</p>", StringComparison.Ordinal), "UI_STRUCTURE", "list is plain text only");
        RejectRml(ValidRml.Replace("<div class=\"list-item\">One / 一</div>", string.Concat(Enumerable.Repeat("<div class=\"list-item\">x</div>", 64)), StringComparison.Ordinal), "UI_LIMIT", "authored list bounded to 64");
        RejectRml(ValidRml.Replace("<body>", "<body>" + string.Concat(Enumerable.Repeat("<div>", 20)), StringComparison.Ordinal)
            .Replace("</body>", string.Concat(Enumerable.Repeat("</div>", 20)) + "</body>", StringComparison.Ordinal), "UI_LIMIT", "XML nesting bounded before tree allocation");
        RejectRml(ValidRml.Replace("<body>", "<body>" + string.Concat(Enumerable.Repeat("<div />", 257)), StringComparison.Ordinal), "UI_LIMIT", "XML element count bounded");

        RejectStyle("body { colro: #ffffff; }", "UI_PROPERTY", "unknown style property rejected");
        RejectStyle("body { color: nope; }", "UI_PROPERTY", "unknown color rejected");
        RejectStyle("body { color: #fff; }", "UI_PROPERTY", "out-of-profile short color rejected");
        RejectStyle("body { opacity: 0.5; }", "UI_PROPERTY", "unlisted property rejected");
        RejectStyle("body { font-family: \"Other Family\"; }", "UI_PROPERTY", "unloaded font family rejected");
        RejectStyle("#missing { height: 10dp; }", "UI_SELECTOR", "unknown selector ID rejected");
        RejectStyle(".missing { height: 10dp; }", "UI_SELECTOR", "unknown selector class rejected");
        RejectStyle("button:visited { color: #ffffff; }", "UI_SELECTOR", "unknown pseudo-class rejected");
        RejectStyle("body, button { color: #ffffff; }", "UI_RCSS", "selector lists explicitly unsupported");
        RejectStyle("#settings-panel button { color: #ffffff; }", "UI_SELECTOR", "unlisted descendant selector rejected");
        RejectStyle("@import \"https://example.invalid/a.rcss\";", "UI_RCSS", "imports rejected");
        RejectStyle("body { background-image: url(https://example.invalid/a.png); }", "UI_RCSS", "URL function rejected");
        RejectStyle("body { color: var(--color); }", "UI_RCSS", "runtime variable rejected");
        RejectStyle("body { transform: translate(1px); }", "UI_RCSS", "transforms rejected");
        RejectStyle("#item-list { border-radius: 6dp; }", "UI_PROPERTY", "rounded scroll clipping rejected");
        RejectStyle("div { border-radius: 6dp; }", "UI_PROPERTY", "indirect rounded scroll clipping rejected");
        RejectStyle("button { overflow-y: auto; }", "UI_PROPERTY", "rounded button scrolling rejected");
        RejectStyle("#settings-panel { overflow-x: hidden; }", "UI_PROPERTY", "unapproved ancestor clipping rejected");
        RejectStyle("body { width: 101%; }", "UI_PROPERTY", "percentage bound enforced");
        RejectStyle("body { height: -1dp; }", "UI_PROPERTY", "negative lengths rejected");
        RejectStyle("body { height: 99999dp; }", "UI_PROPERTY", "length limit enforced");
        RejectStyle("body { font-size: 0dp; }", "UI_PROPERTY", "font size bound enforced");
        RejectStyle("body { padding: 1dp 2dp 3dp 4dp 5dp; }", "UI_PROPERTY", "shorthand count bounded");
        RejectStyle("body { color: #ffffff !important; }", "UI_RCSS", "important rejected");
        RejectStyle("body { color: #ffffff }", "UI_RCSS", "trailing semicolon required");
        RejectStyle("body { color: #ffffff;", "UI_RCSS", "closing brace required");
        RejectStyle("body { color #ffffff; }", "UI_RCSS", "property colon required");
        RejectStyle("body {}", "UI_RCSS", "empty rule rejected");
        RejectStyle("/* only comments */", "UI_RCSS", "style rules required");
        RejectStyle("/* unfinished", "UI_RCSS", "unterminated comment rejected");
        RejectStyle("body { co/*splice*/lor: #ffffff; }", "UI_RCSS", "token-splicing comment rejected");
        RejectStyle("body { color\u00a0: #ffffff; }", "UI_RCSS", "non-ASCII syntax whitespace rejected");
        RejectStyle("body { color: #ffffff; color: #000000; }", "UI_PROPERTY", "duplicate declaration rejected");
        RejectStyle(string.Concat(Enumerable.Repeat("body { color: #ffffff; }", 129)), "UI_LIMIT", "rule count bounded");
        Parse(ValidRml, "button:focus { border-radius: 0dp; }\n#item-list { overflow-y: scroll; }\nbody { width: 100%; font-size: 20px; }");
        check(true, "UI safe button radius, rectangular scrolling, percentages and px supported");
        try { Parse(ValidRml, "body {\n  colro: #ffffff;\n}"); }
        catch (UiAuthoringException e)
        { check(e.Line == 2 && e.Column == 3 && e.Field == "body/colro" && e.FilePath == "fixture.rcss", "UI style diagnostic has exact declaration file/line/column/field"); }
        try { Parse(ValidRml.Replace("id=\"status\"", "id=\"status\" data-oops=\"x\"", StringComparison.Ordinal), ValidRcss); }
        catch (UiAuthoringException e)
        { check(e.Line == 11 && e.Field.EndsWith("p#status@data-oops", StringComparison.Ordinal), "UI markup diagnostic has exact source line and field"); }

        UiSettingsContract.ValidateModel(new string('名', 32), 100, new string('x', 255), Array.Empty<string>());
        UiSettingsContract.ValidateModel(string.Concat(Enumerable.Repeat("😀", 31)), 0, "就绪", new[] { "<& literal text>" });
        check(true, "UI model accepts bounded Chinese and supplementary Unicode without interpreting markup");
        Reject(() => UiSettingsContract.ValidateModel(new string('x', 33), 0, ""), "UI_MODEL", "model Unicode scalar bound");
        Reject(() => UiSettingsContract.ValidateModel(string.Concat(Enumerable.Repeat("😀", 32)), 0, ""), "UI_MODEL", "native name buffer reserves NUL");
        Reject(() => UiSettingsContract.ValidateModel("bad\ud800", 0, ""), "UI_MODEL", "model invalid UTF-16 rejected");
        Reject(() => UiSettingsContract.ValidateModel("bad\0", 0, ""), "UI_MODEL", "model embedded NUL rejected");
        Reject(() => UiSettingsContract.ValidateModel("bad\n", 0, ""), "UI_MODEL", "model control character rejected");
        Reject(() => UiSettingsContract.ValidateModel("", -1, ""), "UI_MODEL", "model volume minimum enforced");
        Reject(() => UiSettingsContract.ValidateModel("", 101, ""), "UI_MODEL", "model volume maximum enforced");
        Reject(() => UiSettingsContract.ValidateModel("", 0, new string('x', 256)), "UI_MODEL", "native status buffer reserves NUL");
        Reject(() => UiSettingsContract.ValidateModel("", 0, "", new string[65]), "UI_MODEL", "prospective authored list count bound");
        Reject(() => UiSettingsContract.ValidateModel("", 0, "", new[] { new string('x', 257) }), "UI_MODEL", "prospective authored list item bound");

        // All filesystem input is created here and removed here; no ambient repository files required.
        string directory = Path.Combine(Path.GetTempPath(), "gal-ui-authoring-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string rmlPath = Path.Combine(directory, "settings.rml"), rcssPath = Path.Combine(directory, "settings.rcss");
            Reject(() => UiAuthoring.ValidateFiles(rmlPath), "UI_FILE", "missing source has structured file diagnostic");
            File.WriteAllText(rmlPath, ValidRml, new UTF8Encoding(false));
            Reject(() => UiAuthoring.ValidateFiles(rmlPath), "UI_FILE", "missing linked stylesheet has structured file diagnostic");
            File.WriteAllText(rcssPath, ValidRcss, new UTF8Encoding(false));
            check(UiAuthoring.ValidateFiles(rmlPath).Items.Count == 2, "UI bounded sibling files validate");
            Reject(() => UiAuthoring.ValidateFiles(rmlPath, Path.Combine(directory, "different.rcss")), "UI_RESOURCE", "different stylesheet cannot substitute for linked file");
            Reject(() => UiAuthoring.ValidateFiles(rmlPath + "\0"), "UI_FILE", "invalid filesystem path has structured diagnostic");
            File.WriteAllBytes(rmlPath, new byte[UiAuthoring.MaxFileBytes + 1]);
            Reject(() => UiAuthoring.ValidateFiles(rmlPath), "UI_SIZE", "oversized file rejected before reading contents");
        }
        finally { Directory.Delete(directory, recursive: true); }

        void RejectRml(string rml, string code, string label) => Reject(() => Parse(rml, ValidRcss), code, label);
        void RejectStyle(string style, string code, string label) => Reject(() => Parse(ValidRml, style), code, label);
        void Reject(Action action, string code, string label)
        {
            try { action(); }
            catch (UiAuthoringException e)
            {
                check(e.Code == code, "UI " + label + " (" + e.Code + ")");
                check(e.Line >= 1 && e.Column >= 1 && e.FilePath.Length > 0 && e.Field.Length > 0 && e.Cause.Length > 0,
                    "UI structured diagnostic includes file/line/column/field/cause: " + label);
                return;
            }
            check(false, "UI expected rejection: " + label);
        }
    }

    private static string WithoutDeclaration() => ValidRml[(ValidRml.IndexOf("?>", StringComparison.Ordinal) + 2)..];
    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);
    private static UiValidatedDocument Parse(string rml, string rcss) => UiAuthoring.Validate(Bytes(rml), Bytes(rcss), "fixture.rml", "fixture.rcss");
}
