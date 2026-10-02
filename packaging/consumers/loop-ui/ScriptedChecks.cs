using System.Numerics;
using System.Runtime.InteropServices;
using Dotnet2DStarter;
using GameAuthoringLab;

// Fixture-only scripted input; the live path has no test clock or SDL declarations.
internal static class ScriptedChecks
{
    public static void Run(EngineHost engine, LoopUiHost host, StarterGame game, UiModelSession<StarterGame> ui)
    {
        int checks = 0;
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException("LOOP UI CHECK: " + name);
            checks++;
        }
        void Near(Vector2 actual, Vector2 expected, string name) => Check(Vector2.Distance(actual, expected) < .01f, name);
        double step = StarterGame.StepSeconds;
        string captures = Path.GetFullPath(Environment.GetEnvironmentVariable("GAL_LOOP_CAPTURE_DIR") ?? "loop-ui-captures");
        Directory.CreateDirectory(captures);
        using var metadata = new StreamWriter(Path.Combine(captures, "captures.tsv"));
        metadata.WriteLine("name\trenderX\trenderY\tsimX\tsimY\tred\tgreen\tblue\tpaused");
        void Capture(string name, Vector2 expected)
        {
            ui.Capture(Path.Combine(captures, name));
            Check(host.Frame(0), "capture frame remains live");
            Near(host.DrawPosition, expected, name + " expected physics-to-render pose");
            Check(File.Exists(Path.Combine(captures, name)), name + " native pixel readback exists");
            metadata.WriteLine(FormattableString.Invariant($"{name}\t{host.DrawPosition.X:R}\t{host.DrawPosition.Y:R}\t{game.Position.X:R}\t{game.Position.Y:R}\t{(game.Paused ? 102 : 255)}\t{(game.Pulses % 2 == 0 ? 204 : 51)}\t77\t{game.Paused.ToString().ToLowerInvariant()}"));
            metadata.Flush();
        }
        Check(ui.Status.Loaded && !ui.Status.Pending && ui.Status.Overflow == 0, "pause model published");
        SdlInput.Focus();
        host.Frame(0); // Neutral frame releases InputActionMap's focus-recovery gate.
        SdlInput.Key(PhysicalKey.D, true);
        for (int i = 0; i < 60; i++) host.Frame(step);
        SdlInput.Key(PhysicalKey.D, false);
        host.Frame(step / 2);
        Check(game.Steps == 60, "real SDL held movement supplies sixty physics ticks");
        Near(game.Position, new(320, 120), "physics copied pose after sixty ticks");
        Check(Math.Abs(host.Alpha - .5) < 1e-6, "half-step interpolation remainder");
        Capture("moving.bmp", new(320 - 160 * StarterGame.StepSeconds / 2, 120));
        Near(game.Position, new(320, 120), "drawing never writes interpolation into physics pose");

        int commands = host.Commands;
        SdlInput.Click(780, 132); // Authored Pause button, through public SDL mouse events.
        host.Frame(.1);
        Check(game.Paused && host.Commands == commands + 1, "real pause button executes");
        Check(game.Steps == 60, "pause command runs before fixed-step catch-up");
        Check(!ui.IsCurrent(host.LastCommand), "toggle advances snapshot and retires the old command");
        Near(host.DrawPosition, game.Position, "pause collapses interpolation history");
        int draws = host.Draws;
        SdlInput.Key(PhysicalKey.D, true);
        SdlInput.Tap(PhysicalKey.Space);
        for (int i = 0; i < 3; i++) host.Frame(.1);
        Check(game.Steps == 60 && game.Pulses == 0 && host.Draws == draws + 3,
            "paused physics stays frozen while UI/render frames continue");
        Near(game.Position, new(320, 120), "pause blocks held movement as well as one-shot gameplay input");
        SdlInput.Key(PhysicalKey.D, false);
        host.Frame(.1); // Release while paused; neither held state nor the tap may replay.
        Capture("paused.bmp", new(320, 120));

        commands = host.Commands;
        SdlInput.Click(780, 188); // Restart works while the fixed-step loop is stopped.
        host.Frame(.1);
        Check(game.Paused && host.Commands == commands + 1 && game.Steps == 0 && game.Pulses == 0,
            "real restart button preserves pause and clears counters");
        Near(game.Position, new(160, 120), "restart teleports authoritative body");
        Capture("restarted.bmp", new(160, 120));
        for (int i = 0; i < 4; i++) { SdlInput.Click(780, 188); host.Frame(.1); }
        Check(host.Restarts == 5 && game.Paused && game.Steps == 0, "repeated restart reuses live game ownership");

        commands = host.Commands;
        SdlInput.Click(780, 132);
        host.Frame(step / 2);
        Check(!game.Paused && host.Commands == commands + 1 && game.Steps == 0,
            "real resume button executes while previously paused, without premature tick");
        Check(!ui.IsCurrent(host.LastCommand), "resume retires the paused revision command");
        Near(host.DrawPosition, new(160, 120), "zero-step resume has no stale interpolation pose");
        host.Frame(step / 2);
        Check(game.Steps == 1 && game.Pulses == 0, "resume never replays paused held movement or one-shot input");
        Near(game.Position, new(160, 120), "restart also cleared native velocity");

        SdlInput.Tap(PhysicalKey.Space);
        host.Frame(step / 4);
        Check(game.Pulses == 0, "zero-step press remains pending");
        SdlInput.Click(780, 188);
        SdlInput.Tap(PhysicalKey.Space);
        host.Frame(.1);
        Check(game.Steps == 0 && game.Pulses == 0 && !game.Paused,
            "restart discards earlier and simultaneous gameplay input and debt");
        host.Frame(step);
        Check(game.Steps == 1 && game.Pulses == 0, "post-restart tick has no stale input");

        commands = host.Commands;
        ulong applies = ui.NativeApplyCalls;
        SdlInput.Click(780, 132);
        SdlInput.Click(780, 132);
        host.Frame(0);
        Check(host.Commands == commands + 2 && !game.Paused && ui.NativeApplyCalls == applies,
            "same-revision UI commands all drain before one unchanged projection");
        Check(ui.Poll().IsEmpty && ui.Status.Overflow == 0 && engine.Textures.Count == 1,
            "command queue drained and texture owner stable through restarts");
        Console.WriteLine($"LOOP UI CHECKS PASS assertions={checks} commands={host.Commands} restarts={host.Restarts} captures={captures}");
        Console.WriteLine("Coverage: real queued SDL events and software pixel readback; no manual/device acceptance or new AOT publish.");
    }
}

// Public pinned SDL 3.4.16 ABI, following the model-ui package consumer pattern.
// This helper neither obtains an engine context nor calls engine-private exports.
internal static unsafe class SdlInput
{
    [StructLayout(LayoutKind.Explicit, Size = 128)]
    private struct Event
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(16)] public uint WindowId;
        [FieldOffset(24)] public byte Button;
        [FieldOffset(25)] public byte ButtonDown;
        [FieldOffset(26)] public byte Clicks;
        [FieldOffset(28)] public float X;
        [FieldOffset(32)] public float Y;
        [FieldOffset(24)] public int Scancode;
        [FieldOffset(28)] public uint Keycode;
        [FieldOffset(36)] public byte KeyDown;
    }
    [DllImport("libSDL3.so.0", CallingConvention = CallingConvention.Cdecl)] private static extern nint* SDL_GetWindows(out int count);
    [DllImport("libSDL3.so.0", CallingConvention = CallingConvention.Cdecl)] private static extern uint SDL_GetWindowID(nint window);
    [DllImport("libSDL3.so.0", CallingConvention = CallingConvention.Cdecl)] private static extern void SDL_free(nint memory);
    [DllImport("libSDL3.so.0", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)] private static extern bool SDL_PushEvent(ref Event value);
    private static uint Window()
    {
        var windows = SDL_GetWindows(out int count);
        try
        {
            if (windows is null || count != 1) throw new InvalidOperationException("Expected one package-owned SDL window");
            return SDL_GetWindowID(windows[0]);
        }
        finally { SDL_free((nint)windows); }
    }
    private static void Push(Event value)
    {
        if (!SDL_PushEvent(ref value)) throw new InvalidOperationException("Public SDL event queue rejected input");
    }
    public static void Focus() => Push(new Event { Type = 0x20e, WindowId = Window() });
    public static void Key(PhysicalKey key, bool down) => Push(new Event {
        Type = down ? 0x300u : 0x301u, WindowId = Window(), Scancode = (int)key,
        Keycode = key switch { PhysicalKey.D => 'd', PhysicalKey.Space => ' ', _ => throw new ArgumentException("Fixture key not declared") },
        KeyDown = down ? (byte)1 : (byte)0
    });
    public static void Tap(PhysicalKey key) { Key(key, true); Key(key, false); }
    public static void Click(float x, float y)
    {
        uint window = Window();
        Push(new Event { Type = 0x400, WindowId = window, X = x, Y = y });
        foreach (bool down in new[] { true, false })
            Push(new Event { Type = down ? 0x401u : 0x402u, WindowId = window, Button = 1,
                ButtonDown = down ? (byte)1 : (byte)0, Clicks = 1, X = x, Y = y });
    }
}
