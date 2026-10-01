using System.Numerics;
using System.Runtime.InteropServices;

namespace GameAuthoringLab;

// Physical SDL3 scancodes, not translated characters. Arbitrary valid codes 1..511
// can be bound; these named values are the controls used by the small examples.
public enum PhysicalKey { A=4, D=7, E=8, F=9, S=22, T=23, W=26, Enter=40, Escape=41, Tab=43, Space=44, F5=62, F9=66, Right=79, Left=80, Down=81, Up=82 }
public enum PointerButton { Left=1, Middle=2, Right=3 }
[Flags] public enum InputFlags : uint { Focused=1, Drawable=2, FocusChanged=4 }
[Flags] public enum InputConsumption : uint { Keyboard=1, Pointer=2, Wheel=4, Text=8 }

[StructLayout(LayoutKind.Sequential)]
public unsafe struct InputSnapshot
{
    public uint Size, Version, Quit;
    public InputFlags Flags;
    public int WindowWidth, WindowHeight, PixelWidth, PixelHeight;
    public float MouseX, MouseY, WheelX, WheelY, GameWheelX, GameWheelY;
    public uint ButtonsDown, ButtonsPressed, ButtonsReleased, GameButtonsDown, GameButtonsPressed, GameButtonsReleased;
    public fixed ulong KeysDown[8]; public fixed ulong KeysPressed[8]; public fixed ulong KeysReleased[8];
    public fixed ulong GameKeysDown[8]; public fixed ulong GameKeysPressed[8]; public fixed ulong GameKeysReleased[8];
    public InputConsumption Consumed; public uint Reserved;
    public readonly bool Focused => (Flags & InputFlags.Focused) != 0;
    public readonly bool Drawable => (Flags & InputFlags.Drawable) != 0;
    public readonly Viewport Viewport => new(WindowWidth, WindowHeight, PixelWidth, PixelHeight, Drawable);
    public readonly Vector2 Pointer => new(MouseX, MouseY);
    public bool KeyDown(PhysicalKey key, bool raw = false) { int i = ValidateKey(key); fixed (ulong* value = KeysDown, game = GameKeysDown) return Has(raw ? value : game, i); }
    public bool KeyPressed(PhysicalKey key, bool raw = false) { int i = ValidateKey(key); fixed (ulong* value = KeysPressed, game = GameKeysPressed) return Has(raw ? value : game, i); }
    public bool KeyReleased(PhysicalKey key, bool raw = false) { int i = ValidateKey(key); fixed (ulong* value = KeysReleased, game = GameKeysReleased) return Has(raw ? value : game, i); }
    private static bool Has(ulong* bits, int key) => (bits[key / 64] & (1ul << (key % 64))) != 0;
    internal static int ValidateKey(PhysicalKey key) => (int)key is > 0 and < 512 ? (int)key : throw new ArgumentOutOfRangeException(nameof(key));
    internal static uint ButtonBit(int button) => button is > 0 and <= 32 ? 1u << (button - 1) : throw new ArgumentOutOfRangeException(nameof(button));
    public readonly bool ButtonDown(PointerButton button, bool raw = false) => ((raw ? ButtonsDown : GameButtonsDown) & ButtonBit((int)button)) != 0;
    public readonly bool ButtonPressed(PointerButton button, bool raw = false) => ((raw ? ButtonsPressed : GameButtonsPressed) & ButtonBit((int)button)) != 0;
    public readonly bool ButtonReleased(PointerButton button, bool raw = false) => ((raw ? ButtonsReleased : GameButtonsReleased) & ButtonBit((int)button)) != 0;
}

/// <summary>Framebuffer pixels are the current renderer's camera/screen units; SDL pointers are window units.</summary>
public readonly record struct Viewport(int WindowWidth, int WindowHeight, int PixelWidth, int PixelHeight, bool Drawable = true)
{
    public bool IsValid => Drawable && WindowWidth is > 0 and <= 16384 && WindowHeight is > 0 and <= 16384
        && PixelWidth is > 0 and <= 16384 && PixelHeight is > 0 and <= 16384;
    private static bool Result(double x, double y, out Vector2 point)
    {
        point = default;
        if (!double.IsFinite(x) || !double.IsFinite(y) || Math.Abs(x) > float.MaxValue || Math.Abs(y) > float.MaxValue) return false;
        point = new((float)x, (float)y); return true;
    }
    private static bool Valid(Camera camera) => float.IsFinite(camera.X) && float.IsFinite(camera.Y) && float.IsFinite(camera.Zoom) && camera.Zoom is >= .01f and <= 100;
    public bool TryWindowToPixels(Vector2 window, out Vector2 pixels)
    { pixels = default; return IsValid && Result((double)window.X * PixelWidth / WindowWidth, (double)window.Y * PixelHeight / WindowHeight, out pixels); }
    public bool TryPixelsToWindow(Vector2 pixels, out Vector2 window)
    { window = default; return IsValid && Result((double)pixels.X * WindowWidth / PixelWidth, (double)pixels.Y * WindowHeight / PixelHeight, out window); }
    public bool TryWindowToWorld(Vector2 window, Camera camera, out Vector2 world)
    { world = default; return IsValid && Valid(camera) && Result(camera.X + (double)window.X * PixelWidth / WindowWidth / camera.Zoom, camera.Y + (double)window.Y * PixelHeight / WindowHeight / camera.Zoom, out world); }
    public bool TryWorldToWindow(Vector2 world, Camera camera, out Vector2 window)
    { window = default; return IsValid && Valid(camera) && Result(((double)world.X - camera.X) * camera.Zoom * WindowWidth / PixelWidth, ((double)world.Y - camera.Y) * camera.Zoom * WindowHeight / PixelHeight, out window); }
}

public readonly record struct ActionState(uint Down, uint Pressed, uint Released);
public readonly record struct InputBinding(uint Action, int Code, bool Pointer = false, bool AllowUiConsumed = false)
{
    public static InputBinding Key(uint action, PhysicalKey key, bool allowUiConsumed = false) => new(action, (int)key, false, allowUiConsumed);
    public static InputBinding Button(uint action, PointerButton button, bool allowUiConsumed = false) => new(action, (int)button, true, allowUiConsumed);
}

/// <summary>
/// Poll-based bindings, independent of entities. Actions are caller-chosen single bits.
/// Alternative controls combine into one held action. A tap that begins/ends in one
/// poll still reports pressed+released. Edges coalesce; there is no ordered event queue.
/// Rebinding/focus loss waits for neutral controls, preventing held-key activation.
/// </summary>
public sealed class InputActionMap
{
    private InputBinding[] _bindings;
    private bool[] _previousControls;
    private uint _previous;
    private bool _waitForNeutral;
    public InputActionMap(params InputBinding[] bindings) { _bindings = Copy(bindings); _previousControls = new bool[_bindings.Length]; }
    public void Rebind(params InputBinding[] bindings) { var copy = Copy(bindings); var previous = new bool[copy.Length]; _bindings = copy; _previousControls = previous; _previous = 0; _waitForNeutral = true; }
    private static InputBinding[] Copy(InputBinding[] bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        if (bindings.Length > 128) throw new ArgumentException("At most 128 bindings per action map.", nameof(bindings));
        foreach (var binding in bindings)
        {
            if (binding.Action == 0 || (binding.Action & (binding.Action - 1)) != 0) throw new ArgumentException("An action must be one nonzero bit.", nameof(bindings));
            if (binding.Pointer) InputSnapshot.ButtonBit(binding.Code); else InputSnapshot.ValidateKey((PhysicalKey)binding.Code);
        }
        return (InputBinding[])bindings.Clone();
    }
    public ActionState Update(InputSnapshot snapshot)
    {
        if (!snapshot.Focused) { uint released = _previous; Array.Clear(_previousControls); _previous = 0; _waitForNeutral = true; return new(0, 0, released); }
        uint down = 0, presses = 0, releases = 0, continuous = 0; bool rawActivity = false;
        for (int i = 0; i < _bindings.Length; i++)
        {
            var binding = _bindings[i];
            bool held, pressed, released;
            if (binding.Pointer)
            {
                var button = (PointerButton)binding.Code;
                held = snapshot.ButtonDown(button, binding.AllowUiConsumed); pressed = snapshot.ButtonPressed(button, binding.AllowUiConsumed); released = snapshot.ButtonReleased(button, binding.AllowUiConsumed);
                rawActivity |= snapshot.ButtonDown(button, true) || snapshot.ButtonPressed(button, true);
            }
            else
            {
                var key = (PhysicalKey)binding.Code;
                held = snapshot.KeyDown(key, binding.AllowUiConsumed); pressed = snapshot.KeyPressed(key, binding.AllowUiConsumed); released = snapshot.KeyReleased(key, binding.AllowUiConsumed);
                rawActivity |= snapshot.KeyDown(key, true) || snapshot.KeyPressed(key, true);
            }
            if (_previousControls[i] && held && !released) continuous |= binding.Action;
            _previousControls[i] = held;
            if (held) down |= binding.Action; if (pressed) presses |= binding.Action; if (released) releases |= binding.Action;
        }
        if (_waitForNeutral) { if (!rawActivity) _waitForNeutral = false; return default; }
        uint cycle = presses & releases & ~continuous;
        uint began = ((down | presses) & ~_previous) | cycle;
        uint ended = ((_previous | (began & releases)) & ~down) | (cycle & _previous);
        _previous = down;
        return new(down, began, ended);
    }

    // Default bindings belong to this sample adapter, not the native backend or World.
    public static InputActionMap CreateSample() => new(
        InputBinding.Key(Native.Left, PhysicalKey.Left), InputBinding.Key(Native.Left, PhysicalKey.A),
        InputBinding.Key(Native.Right, PhysicalKey.Right), InputBinding.Key(Native.Right, PhysicalKey.D),
        InputBinding.Key(Native.Up, PhysicalKey.Up), InputBinding.Key(Native.Up, PhysicalKey.W),
        InputBinding.Key(Native.Down, PhysicalKey.Down), InputBinding.Key(Native.Down, PhysicalKey.S),
        InputBinding.Key(Native.Space, PhysicalKey.Space), InputBinding.Key(Native.Escape, PhysicalKey.Escape, true),
        InputBinding.Key(Native.Interact, PhysicalKey.E), InputBinding.Key(Native.Drop, PhysicalKey.F),
        InputBinding.Key(Native.Transition, PhysicalKey.T), InputBinding.Key(Native.Save, PhysicalKey.F5), InputBinding.Key(Native.Load, PhysicalKey.F9));
}
