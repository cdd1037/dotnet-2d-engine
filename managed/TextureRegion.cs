namespace GameAuthoringLab;

internal readonly record struct BitmapInfo(string Path, int Width, int Height);

/// <summary>Integer texels, top-left origin. No rotated/trimmed packing or implicit padding.</summary>
internal readonly record struct TextureRegion(int X, int Y, int Width, int Height)
{
    public void Validate(int textureWidth, int textureHeight)
    {
        if (X < 0 || Y < 0 || Width < 1 || Height < 1
            || (long)X + Width > textureWidth || (long)Y + Height > textureHeight)
            throw new ArgumentOutOfRangeException(nameof(TextureRegion), "Source rectangle must be positive and contained in the texture.");
    }
}

internal sealed record TextureAsset(string Path, TextureRegion? Region = null);
internal readonly record struct TextureBinding(ulong Handle, TextureRegion? Region = null);
