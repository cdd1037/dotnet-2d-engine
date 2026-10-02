using System.Runtime.CompilerServices;
using GameAuthoringLab;
namespace CardUiOurs;

[UiModel]
internal sealed class GeneratedItem
{
    public ulong Id { get; set; } = ulong.MaxValue;
    public string Title { get; set; } = "Generated item";
    public double Number { get; set; } = 2;
}
[UiModel]
internal sealed class GeneratedView
{
    [UiField(Maximum = 4)] public List<GeneratedItem> Items { get; } = [new()];
}
[UiContract(typeof(GeneratedView), "assets/ui/generated-check.rml")]
internal sealed partial class GeneratedFixture
{
    public GeneratedView Model { get; } = new();
    public UiModelSession<GeneratedView> Session { get; }
    public ulong Selected { get; private set; }
    public double LastNumber { get; private set; }
    public GeneratedFixture(EngineHost engine) => Session = new(engine, CreateUiSchema(), CreateUiCommands());
    [UiCommand] private void Choose(ulong id) => Selected = id;
    [UiCommand] private void Measure(double number) => LastNumber = number;
}

internal static class GeneratedChecks
{
    public static void Run(EngineHost engine, AssetRoot assets)
    {
        int assertions = 0;
        void Check(bool valid, string message) { if (!valid) throw new Exception("GENERATED UI: " + message); assertions++; }
        void Draw() => engine.Draw(new Camera { Zoom = 1 }, ReadOnlySpan<SpriteCommand>.Empty);
        UiCommandEvent previousPacket = default;
        var fixture = new GeneratedFixture(engine);
        using (var ui = fixture.Session)
        {
            ui.StageAsset(assets, "ui/generated-check.rml", fixture.Model);
            Check(ui.Status is { Loaded: false, Pending: true, Revision: 0 }, "generated initial model waits for the normal draw");
            Draw();
            Check(ui.Status is { Loaded: true, Pending: false, Revision: 1 } && !ui.Apply(fixture.Model), "one frame publishes generated model and unchanged baseline");
            SdlInput.Focus(); engine.PollInputFrame();
            SdlInput.Click(90, 40); engine.PollInputFrame(); Draw();
            var choose = ui.Poll(); previousPacket = choose;
            Check(!choose.IsEmpty && ui.Dispatch(choose) && fixture.Selected == ulong.MaxValue, "generated PascalCase key command routes exact ulong");
            SdlInput.Click(310, 40); engine.PollInputFrame(); Draw();
            var measure = ui.Poll();
            Check(!measure.IsEmpty && measure.CommandId != choose.CommandId && ui.Dispatch(measure) && fixture.LastNumber == 2, "generated Number command has distinct automatic ID");
            fixture.Model.Items[0].Title = "Updated"; ui.Apply(fixture.Model); Draw();
            Check(!ui.Dispatch(choose), "generated registrations preserve stale revision rejection");
            fixture.Model.Items[0].Title = "bad\ud800";
            var beforeInvalid = ui.Status;
            try { ui.StageAsset(assets, "ui/generated-check.rml", fixture.Model); throw new Exception("Expected generated projection diagnostic"); }
            catch (UiAuthoringException e)
            {
                Check(e.Code == "UI_MODEL_VALUE" && e.Field == "state.Items[0].Title", "generated initial projection retains model breadcrumb");
                Check(ui.Status.Generation == beforeInvalid.Generation && ui.Revision == beforeInvalid.Revision && !ui.Status.Pending,
                    "invalid generated replacement preserves the live generation and revision");
                Check(e.Declaration?.FilePath.EndsWith("GeneratedChecks.cs", StringComparison.Ordinal) == true && e.Declaration?.Line == 9,
                    "generated projection points at authored C# property");
            }
            fixture.Model.Items[0].Title = "Valid";
            fixture.Model.Items[0].Number = double.NaN;
            try { ui.Apply(fixture.Model); throw new Exception("Expected invalid number diagnostic"); }
            catch (UiAuthoringException e)
            { Check(e.Field == "state.Items[0].Number" && e.Declaration?.Line == 10 && e.InnerException is ArgumentOutOfRangeException, "generated Number checks original declaration"); }
            fixture.Model.Items.Clear(); ui.StageAsset(assets, "ui/generated-check.rml", fixture.Model);
            Check(ui.Status.Loaded && ui.Status.Pending, "replacement retains live document until draw");
            Draw();
            Check(ui.Revision == 1 && !ui.Apply(fixture.Model) && !ui.Dispatch(measure), "empty generated replacement resets revision and rejects retired generation");
            Check(ui.Status.Loaded && ui.Status.Diagnostic.Length == 0, "generated empty collection remains usable");
        }
        var (retained, capture) = Capture(engine);
        Check(!retained.IsCurrent(previousPacket) && !retained.Dispatch(previousPacket), "fresh owner rejects prior-owner packet before loading");
        retained.Dispose(); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Check(!capture.IsAlive, "disposed generated session releases method-group controller capture");
        GC.KeepAlive(retained);
        Console.WriteLine($"GENERATED UI PUBLIC PASS assertions={assertions}");
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (UiModelSession<GeneratedView>, WeakReference) Capture(EngineHost engine)
    {
        var fixture = new GeneratedFixture(engine);
        return (fixture.Session, new WeakReference(fixture));
    }
}
