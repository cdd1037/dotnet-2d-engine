namespace GameAuthoringLab;

// A second closed profile, sharing only bounded XML/CSS tokenization with settings.
// No scripts, arbitrary resources, expressions, public DOM or dynamic bindings.
internal static class GameUiAuthoring
{
    internal static readonly Dictionary<string,(string Tag,string Parent)> Elements=new(StringComparer.Ordinal)
    {
        ["hud"]=("div","body"),["hud-objective"]=("p","hud"),["hud-status"]=("p","hud"),["hud-time"]=("p","hud"),["game-pause"]=("button","hud"),
        ["game-panel"]=("div","body"),["game-title"]=("h1","game-panel"),["game-objective"]=("p","game-panel"),["game-status"]=("p","game-panel"),["game-time"]=("p","game-panel"),["game-actions"]=("div","game-panel"),
        ["game-start"]=("button","game-actions"),["game-resume"]=("button","game-actions"),["game-save"]=("button","game-actions"),["game-load"]=("button","game-actions"),["game-restart"]=("button","game-actions"),["game-menu"]=("button","game-actions")
    };
    internal static bool IsStyleSelector(string value)=>value is "body" or "div" or "h1" or "p" or "button" or "button:hover" or "button:focus" or "button:active" || value.StartsWith('#')&&Elements.ContainsKey(value[1..]);
    public static (string Rml,string Rcss) ValidateFiles(string path)
    {
        try
        {
            var assets = new AssetRoot(Path.GetDirectoryName(Path.GetFullPath(path))!);
            return ValidateAsset(assets, Path.GetFileName(path));
        }
        catch (Exception e) when (e is AssetException or ArgumentException or NotSupportedException)
        { throw new UiAuthoringException("UI_FILE", path, 1, 1, "$", e.Message, e); }
    }
    public static (string Rml, string Rcss) ValidateAsset(AssetRoot assets, string logicalPath)
    {
        var files = UiAuthoring.ReadAssetFiles(assets, logicalPath, "game.rcss");
        return Validate(files.Rml, files.Rcss, files.RmlFile, files.RcssFile);
    }
    internal static (string Rml,string Rcss) Validate(ReadOnlySpan<byte> rml,ReadOnlySpan<byte> css,string file="game.rml",string cssFile="game.rcss")
    {
        string markup=UiAuthoring.Decode(rml,file),style=UiAuthoring.Decode(css,cssFile);
        var root=UiAuthoring.ParseXml(markup,file).Root??throw Error(file,null,"Missing root.");
        void Require(bool condition,UiXmlElement node,string cause){if(!condition)throw Error(file,node,cause);}
        Require(root.Name=="rml",root,"Expected rml root.");
        Require(root.Elements().Select(n=>n.Name.LocalName).SequenceEqual(new[]{"head","body"}),root,"Expected head and body.");
        var head=root.Element("head")!;var body=root.Element("body")!;
        Require(head.Elements().Select(n=>n.Name.LocalName).SequenceEqual(new[]{"title","link"}),head,"Expected title and sibling stylesheet link.");
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var node in root.DescendantsAndSelf())
        {
            Require(node.Name.NamespaceName.Length==0,node,"Namespaces are unsupported.");
            string tag=node.Name.LocalName;string? id=node.Attribute("id")?.Value;
            if(node==root||node==head||node==body||node.Parent==head)
            {
                Require(id is null,node,"Root/head/body cannot carry IDs.");
                if(tag=="link")Require(node.Attribute("href")?.Value=="game.rcss"&&node.Attribute("type")?.Value=="text/rcss",node,"Only sibling game.rcss is allowed.");
                foreach(var a in node.Attributes())Require(tag=="link"&&a.Name is var name&&(name=="type"||name=="href"),node,"Unexpected attribute.");
            }
            else
            {
                Require(id is not null&&Elements.ContainsKey(id),node,"Unknown or missing fixed game element ID.");
                var expected=Elements[id!];Require(seen.Add(id!),node,"Duplicate ID.");Require(tag==expected.Tag,node,"Wrong element type for "+id);
                Require((node.Parent?.Attribute("id")?.Value??node.Parent?.Name.LocalName)==expected.Parent,node,"Wrong parent for "+id);
                foreach(var a in node.Attributes())Require(a.Name=="id",node,"Only id attributes are supported.");
            }
            if(tag is "h1" or "p" or "button" or "title" or "link")Require(!node.HasElements,node,"Text elements cannot contain markup.");
            foreach(var text in node.Nodes().OfType<UiXmlText>())
            {
                if(string.IsNullOrWhiteSpace(text.Value))continue;
                Require(tag is "h1" or "p" or "button" or "title",node,"Unexpected container text.");
                Require(!text.Value.Contains("{{",StringComparison.Ordinal)&&!text.Value.Contains("}}",StringComparison.Ordinal),node,"Binding expressions are unsupported.");
                UiSettingsContract.ValidateText(text.Value.Trim(),512,256,"game text");
            }
        }
        Require(seen.Count==Elements.Count,body,"Missing required game UI elements.");
        UiAuthoring.ValidateGameStyle(style,cssFile);return(markup,style);
    }
    private static UiAuthoringException Error(string file,UiXmlElement? node,string cause)=>new("GAME_UI_PROFILE",file,node?.LineNumber??1,node?.LinePosition??1,node?.Attribute("id")?.Value??node?.Name.LocalName??"$",cause);
}
