using System.Numerics;
namespace GameAuthoringLab;

// These handles borrow an existing owner; they neither retain a native resource nor
// transfer disposal responsibility. A copied handle becomes invalid with its lease.
internal interface ITextureSource
{
    ulong ResolveTexture(EngineHost engine, TextureRegion? region);
}

/// <summary>Borrowed texture/sample view. Default means an untextured solid sprite.</summary>
public readonly record struct TextureHandle
{
    private readonly ITextureSource? source;
    internal TextureHandle(ITextureSource source) => this.source = source;
    internal ulong Resolve(EngineHost engine, TextureRegion? region)
    {
        if (source is not null) return source.ResolveTexture(engine, region);
        if (region is not null) throw new ArgumentException("A source region requires a texture.", nameof(region));
        return 0;
    }
}

/// <summary>Borrowed material view. Default selects the built-in sprite material.</summary>
public readonly record struct MaterialHandle
{
    private readonly MaterialLease? lease;
    internal MaterialHandle(MaterialLease lease) => this.lease = lease;
    internal ulong Resolve(EngineHost engine) => lease?.ResolveMaterial(engine) ?? 0;
}

/// <summary>Borrowed render attachment. Default is invalid; use FramePass.Window for the window.</summary>
public readonly record struct RenderTargetHandle
{
    private readonly RenderTarget? target;
    internal RenderTargetHandle(RenderTarget target) => this.target = target;
    internal ulong Resolve(EngineHost engine) => target?.ResolveTarget(engine)
        ?? throw new ArgumentException("A render target must come from a live RenderTarget owner.");
}

/// <summary>Managed sprite description. No ABI headers or interchangeable numeric resource IDs.</summary>
public readonly record struct SpriteCommand(Transform2D Transform, Vector2 Size, Vector4 Tint, TextureHandle Texture = default)
{
    public TextureRegion? Region { get; init; }
    public bool FlipX { get; init; }
    public bool FlipY { get; init; }
    public MaterialHandle Material { get; init; }
    public MaterialParameters Parameters { get; init; }

    internal MaterialDraw ToNative(EngineHost engine)
    {
        Transform.Validate();
        if (!float.IsFinite(Size.X) || !float.IsFinite(Size.Y) || Size.X < 0 || Size.Y < 0)
            throw new ArgumentOutOfRangeException(nameof(Size), "Sprite size must be finite and nonnegative.");
        ValidateColor(Tint);
        ulong texture = Texture.Resolve(engine, Region), material = Material.Resolve(engine);
        if (material == 0 && (Parameters.First != default || Parameters.Second != default))
            throw new ArgumentException("The built-in material requires zero parameters.", nameof(Parameters));
        Transform.GetBasis(out float m11, out float m12, out float m21, out float m22);
        var draw = new SpriteDraw { M11 = m11, M12 = m12, M21 = m21, M22 = m22,
            X = Transform.X, Y = Transform.Y, Width = Size.X, Height = Size.Y,
            R = Tint.X, G = Tint.Y, B = Tint.Z, A = Tint.W, Texture = texture };
        // Headless image leases validate their region above but intentionally have no native texture.
        return MaterialDraw.Create(SpriteDrawV2.Create(draw, texture == 0 ? null : Region, FlipX, FlipY), material, Parameters);
    }
    internal static void ValidateColor(Vector4 color)
    {
        if (!float.IsFinite(color.X) || !float.IsFinite(color.Y) || !float.IsFinite(color.Z)
            || !float.IsFinite(color.W) || color.X < 0 || color.X > 1 || color.Y < 0 || color.Y > 1
            || color.Z < 0 || color.Z > 1 || color.W < 0 || color.W > 1)
            throw new ArgumentOutOfRangeException(nameof(color), "Color channels must be finite and between zero and one.");
    }
}

/// <summary>One explicit pass over a shared SpriteCommand span. Ranges partition the array; the last pass is Window.</summary>
public readonly struct FramePass
{
    private readonly bool configured, window;
    private readonly RenderTargetHandle target;
    public Camera Camera { get; }
    public int FirstDraw { get; }
    public int DrawCount { get; }
    public Vector4 ClearColor { get; }
    private FramePass(bool window, RenderTargetHandle target, Camera camera, int firstDraw, int drawCount, Vector4 clearColor)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(firstDraw);
        ArgumentOutOfRangeException.ThrowIfNegative(drawCount);
        this.window = window; this.target = target; Camera = camera; FirstDraw = firstDraw; DrawCount = drawCount;
        ClearColor = clearColor; configured = true;
    }
    public static FramePass Window(Camera camera, int firstDraw, int drawCount, Vector4 clearColor = default)
        => new(true, default, camera, firstDraw, drawCount, clearColor);
    public static FramePass ToTarget(RenderTargetHandle target, Camera camera, int firstDraw, int drawCount, Vector4 clearColor = default)
        => new(false, target, camera, firstDraw, drawCount, clearColor);
    internal RenderPass ToNative(EngineHost engine)
    {
        if (!configured) throw new ArgumentException("Use FramePass.Window or FramePass.ToTarget to describe a pass.");
        SpriteCommand.ValidateColor(ClearColor);
        return RenderPass.Create(window ? 0 : target.Resolve(engine), Camera, FirstDraw, DrawCount, ClearColor);
    }
}
