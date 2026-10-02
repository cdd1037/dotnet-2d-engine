using CardUiOurs;
using GameAuthoringLab;

string? font = Environment.GetEnvironmentVariable("GAL_UI_FONT");
if (string.IsNullOrWhiteSpace(font) || !File.Exists(font))
    throw new InvalidOperationException("Set GAL_UI_FONT to a readable compatible licensed font");
var assets = new AssetRoot(Path.Combine(AppContext.BaseDirectory, "assets"));
bool check = args.Contains("--check");
string outputDirectory = Path.GetFullPath(Environment.GetEnvironmentVariable("GAL_UI_ERGONOMICS_OUTPUT")
    ?? Path.Combine(Path.GetTempPath(), "dotnet2d-ui-ergonomics-" + Guid.NewGuid().ToString("N")));
if (check) Directory.CreateDirectory(outputDirectory);
string savePath = check ? Path.Combine(outputDirectory, "cards-save.json")
    : Path.GetFullPath(Environment.GetEnvironmentVariable("CARD_SAVE_PATH") ?? "cards-save.json");
using var engine = EngineHost.Create(maxSprites: 1);
using var ui = new CardUi(engine, assets, savePath);
if (check)
{
    string catalog = File.ReadAllText(assets.Resolve("catalog.json"));
    RulesChecks.Run(catalog);
    Smoke.Run(engine, ui, savePath, outputDirectory);
    DiscardChecks.Run(ui, catalog, savePath, outputDirectory);
    ui.Dispose(); // The diagnostics fixture needs the engine's sole UI owner.
    DiagnosticChecks.Run(engine, outputDirectory);
    Console.WriteLine($"UI ERGONOMICS CHECKS PASS captures={outputDirectory}");
    return;
}
while (ui.Frame()) Thread.Sleep(8);
