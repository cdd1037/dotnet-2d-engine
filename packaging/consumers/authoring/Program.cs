using GameAuthoringLab;

// Ordinary PackageReference consumer: no friend access, unsafe blocks, ABI structs,
// numeric resource identities, caller-allocated action bits, or engine source files.
var checks = new Checks();
var assets = new AssetRoot(Path.Combine(AppContext.BaseDirectory, "assets"));
using var engine = EngineHost.Create(headless: true, maxSprites: 16);
DrawingChecks.Run(checks, engine, assets);
InputChecks.Run(checks, engine);
long inputBytes = InputChecks.MeasureWarm(engine);
long drawBytes = DrawingChecks.MeasureWarm(engine, assets);
checks.That(inputBytes == 0, "warm safe input and typed synthetic states allocate zero bytes");
checks.That(drawBytes == 0, "warm managed drawing paths allocate zero bytes");
checks.That(engine.Textures.Count == 0 && engine.Materials.Count == 0 && engine.RenderTargets.Count == 0,
    "all consumer-owned render resources released");
engine.Dispose();
DrawingChecks.CheckPreviousEngine(checks, assets);
Console.WriteLine($"PACKAGE AUTHORING PASS assertions={checks.Count} input-bytes={inputBytes} draw-bytes={drawBytes} textures=0 materials=0 targets=0");

internal sealed class Checks
{
    internal int Count { get; private set; }
    internal void That(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException("PACKAGE AUTHORING: " + label);
        Count++;
    }
    internal void Reject<T>(Action action, string label) where T : Exception
    {
        try { action(); }
        catch (T) { Count++; return; }
        throw new InvalidOperationException("PACKAGE AUTHORING accepted: " + label);
    }
}
