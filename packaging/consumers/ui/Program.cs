using GameAuthoringLab;
var assets = new AssetRoot(Path.Combine(AppContext.BaseDirectory, "assets"));
using var engine = EngineHost.Create(maxSprites: 32);
var bag = new Bag("Package consumer / 独立使用", [new(700, "Scout / 侦察员"), new(800, "Medic / 医疗员", Selected: true)]);
UiRecord<Bag> Schema() => new UiRecord<Bag>().Text("title", m => m.Title)
    .Array("items", m => m.Items, new UiRecord<Member>().Key("id", m => m.Id).Text("text", m => m.Text)
        .Boolean("enabled", m => m.Enabled).Boolean("selected", m => m.Selected));
UiCommands Commands() => new UiCommands().On("select", UiArgs.Key, (ulong id) =>
    { if (!bag.Items.Any(x => x.Id == id && x.Enabled)) return; })
    .On("add", () => { });
using var ui = new UiModelSession<Bag>(engine, Schema(), Commands());
void Draw() => engine.Draw(new Camera { Zoom = 1 }, ReadOnlySpan<SpriteCommand>.Empty);
ui.StageAsset(assets, "ui/roster.rml", bag); Draw();
if (ui.Apply(bag)) throw new Exception("Unchanged initial model was applied again.");
var status = ui.Status;
if (!status.Loaded || status.Revision != 1 || status.Overflow != 0) throw new Exception("UI did not publish.");
bag.Items.RemoveAt(0); bag.Items.Add(new(900, "New entry / 新成员"));
if (!ui.Apply(bag) || ui.Apply(bag)) throw new Exception("Dirty batch contract failed."); Draw();
if (ui.Revision != 2) throw new Exception("Changed model did not advance revision.");
ui.Capture(Path.Combine(Environment.CurrentDirectory, "package-ui.bmp")); Draw();
ui.StageAsset(assets, "ui/roster.rml", bag); Draw();
if (ui.Status.Generation == status.Generation || ui.Revision != 1 || ui.Apply(bag)) throw new Exception("Replacement did not publish its initial model.");
ui.Dispose();
using (var replacement = new UiModelSession<Bag>(engine, Schema(), Commands()))
{
    replacement.StageAsset(assets, "ui/roster.rml", bag); Draw(); ui.Dispose();
    if (!replacement.Status.Loaded || replacement.Revision != 1) throw new Exception("Obsolete owner closed replacement.");
}
Console.WriteLine("PACKAGE UI PASS schema=typed rows=2 replacement=safe");
sealed record Bag(string Title, List<Member> Items);
sealed record Member(ulong Id, string Text, bool Enabled = true, bool Selected = false);
