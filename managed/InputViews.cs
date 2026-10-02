using System.Numerics;

namespace GameAuthoringLab;

/// <summary>
/// A copied input poll, independent of later polls and the engine's lifetime.
/// Game observes UI-filtered input; Raw is an explicit opt-in to physical input.
/// Neither view is an ordered event queue or a count of repeated taps.
/// </summary>
public readonly struct InputFrame
{
    private readonly InputSnapshot _snapshot;
    internal InputFrame(InputSnapshot snapshot) => _snapshot = snapshot;
    internal InputSnapshot Snapshot => _snapshot;

    public bool Quit => _snapshot.Quit != 0;
    public bool Focused => _snapshot.Focused;
    public bool FocusChanged => (_snapshot.Flags & InputFlags.FocusChanged) != 0;
    public bool Drawable => _snapshot.Drawable;
    public Viewport Viewport => _snapshot.Viewport;
    /// <summary>Pointer position in logical window units, shared by both input views.</summary>
    public Vector2 Pointer => _snapshot.Pointer;
    public InputConsumption Consumed => _snapshot.Consumed;
    public InputView Game => new(_snapshot, raw: false);
    public InputView Raw => new(_snapshot, raw: true);
}

/// <summary>A copied, allocation-free primitive view of either game-routed or raw input.</summary>
public readonly struct InputView
{
    private readonly InputSnapshot _snapshot;
    private readonly bool _raw;
    internal InputView(InputSnapshot snapshot, bool raw) { _snapshot = snapshot; _raw = raw; }

    public Vector2 Wheel => _raw ? new(_snapshot.WheelX, _snapshot.WheelY) : new(_snapshot.GameWheelX, _snapshot.GameWheelY);
    public bool KeyDown(PhysicalKey key) => _snapshot.KeyDown(key, _raw);
    public bool KeyPressed(PhysicalKey key) => _snapshot.KeyPressed(key, _raw);
    public bool KeyReleased(PhysicalKey key) => _snapshot.KeyReleased(key, _raw);
    public bool ButtonDown(PointerButton button) => _snapshot.ButtonDown(button, _raw);
    public bool ButtonPressed(PointerButton button) => _snapshot.ButtonPressed(button, _raw);
    public bool ButtonReleased(PointerButton button) => _snapshot.ButtonReleased(button, _raw);
}
