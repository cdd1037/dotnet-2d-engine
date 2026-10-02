using System.Runtime.InteropServices;
using GameAuthoringLab;
namespace Relay;

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
    public static void Focus(bool focused = true) => Push(new Event { Type = focused ? 0x20eu : 0x20fu, WindowId = Window() });
    public static void Key(PhysicalKey key, bool down) => Push(new Event {
        Type = down ? 0x300u : 0x301u, WindowId = Window(), Scancode = (int)key,
        Keycode = key switch { PhysicalKey.D => 'd', PhysicalKey.E => 'e', PhysicalKey.F => 'f', PhysicalKey.T => 't', PhysicalKey.Space => ' ', PhysicalKey.Escape => 27, PhysicalKey.F5 => 0x4000003e, PhysicalKey.F9 => 0x40000042, _ => throw new ArgumentException("Fixture key not declared") },
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
