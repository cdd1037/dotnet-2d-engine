using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace GameAuthoringLab;

// Keep these blittable declarations in lockstep with native/include/gal.h.
// Pointer parameters are borrowed only for the duration of a native call.
[StructLayout(LayoutKind.Sequential)]
internal struct Config
{
    public uint Size, AbiVersion;
    public int Width, Height;
    public uint MaxSprites, Flags;

    public static unsafe Config Create(bool headless, uint maxSprites = 1024) => new()
    {
        Size = (uint)sizeof(Config), AbiVersion = 1,
        Width = 960, Height = 540, MaxSprites = maxSprites,
        Flags = headless ? Native.Headless : Native.Audio
    };
}

[StructLayout(LayoutKind.Sequential)]
public struct Camera
{
    public float X, Y, Zoom;
}

[StructLayout(LayoutKind.Sequential)]
public struct Sprite
{
    public float X, Y, Width, Height, R, G, B, A;
}

/// <summary>Advanced ABI-shaped draw. Prefer SpriteCommand for ordinary managed authoring.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct SpriteDraw
{
    public float M11, M12, M21, M22, X, Y, Width, Height, R, G, B, A;
    public ulong Texture;
}

/// <summary>Advanced versioned ABI draw. Prefer SpriteCommand for ordinary managed authoring.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct SpriteDrawV2
{
    public uint Size, Version;
    public SpriteDraw Draw;
    public int SourceX, SourceY, SourceWidth, SourceHeight;
    public uint Flags, Reserved;
    public static unsafe SpriteDrawV2 Create(SpriteDraw draw, TextureRegion? region = null, bool flipX = false, bool flipY = false) => new()
    {
        Size = (uint)sizeof(SpriteDrawV2), Version = 2, Draw = draw,
        SourceX = region?.X ?? 0, SourceY = region?.Y ?? 0,
        SourceWidth = region?.Width ?? 0, SourceHeight = region?.Height ?? 0,
        Flags = (flipX ? 1u : 0) | (flipY ? 2u : 0)
    };
}
[StructLayout(LayoutKind.Sequential)]
internal struct TextureInfo { public uint Size; public int Width, Height; public uint Reserved; }

[StructLayout(LayoutKind.Sequential)]
internal struct Input
{
    public uint Size, Quit, Keys;
    public float Wheel, MouseX, MouseY;
    public int Width, Height;
}

[StructLayout(LayoutKind.Sequential)]
public struct Stats
{
    public uint Size, Frames, Sprites, DrawCalls, AudioPlays;
}

internal static unsafe partial class Native
{
    private const string Library = "gal";
    public const uint Headless = 1, Audio = 2;
    public const uint Left = 1, Right = 2, Up = 4, Down = 8, Space = 16, Escape = 32, Interact = 64, Drop = 128, Transition = 256, Save = 512, Load = 1024, FocusLost = 2048;

    [LibraryImport(Library, EntryPoint = "gal_abi_version")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial uint AbiVersion();

    [LibraryImport(Library, EntryPoint = "gal_last_error")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial byte* LastError();

    [LibraryImport(Library, EntryPoint = "gal_create")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int Create(Config* config, nint* context);

    [LibraryImport(Library, EntryPoint = "gal_destroy")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int Destroy(nint context);

    [LibraryImport(Library, EntryPoint = "gal_backend")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial byte* Backend(nint context);

    [LibraryImport(Library, EntryPoint = "gal_poll")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int Poll(nint context, Input* input);

    [LibraryImport(Library, EntryPoint = "gal_poll_v2")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int PollV2(nint context, InputSnapshot* input);

    [LibraryImport(Library, EntryPoint = "gal_begin")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int Begin(nint context, Camera* camera);

    [LibraryImport(Library, EntryPoint = "gal_submit")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int Submit(nint context, Sprite* sprites, uint count);

    [LibraryImport(Library, EntryPoint = "gal_submit_draws")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int SubmitDraws(nint context, SpriteDraw* draws, uint count);

    [LibraryImport(Library, EntryPoint = "gal_submit_draws_v2")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int SubmitDrawsV2(nint context, SpriteDrawV2* draws, uint count);

    [LibraryImport(Library, EntryPoint = "gal_texture_get_info")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int GetTextureInfo(nint context, ulong texture, TextureInfo* info);

    [LibraryImport(Library, EntryPoint = "gal_texture_load_bmp", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int LoadTexture(nint context, string path, ulong* texture);

    [LibraryImport(Library, EntryPoint = "gal_texture_release")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int ReleaseTexture(nint context, ulong texture);

    [LibraryImport(Library, EntryPoint = "gal_texture_count")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int TextureCount(nint context, uint* count);

    [LibraryImport(Library, EntryPoint = "gal_end")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int End(nint context);

    [LibraryImport(Library, EntryPoint = "gal_abort")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int Abort(nint context);

    [LibraryImport(Library, EntryPoint = "gal_play_tone")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int PlayTone(nint context);

    [LibraryImport(Library, EntryPoint = "gal_get_stats")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int GetStats(nint context, Stats* stats);

    // Copy immediately: the native buffer is not owned by the caller.
    public static string Error() => Utf8(LastError());
    public static string Utf8(byte* value) => Marshal.PtrToStringUTF8((nint)value) ?? "";

    public static void Check(int result, string operation)
    {
        if (result != 0)
            throw new InvalidOperationException($"{operation}: {Error()}");
    }
}
