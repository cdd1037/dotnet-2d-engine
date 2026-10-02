using System.Text;
using GameAuthoringLab;

namespace Relay;

// Save/mission schema validation belongs to this application. These bounded
// checks intentionally match the original schema without calling engine internals.
internal static class RelayValidation
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static void ValidateText(string text, int maxBytes, int maxScalars, string field)
    {
        if (text is null) throw TextError(field, "Text cannot be null.");
        if (text.Length > maxBytes) throw TextError(field, $"Text exceeds {maxBytes} UTF-8 bytes.");
        try
        {
            if (StrictUtf8.GetByteCount(text) > maxBytes)
                throw TextError(field, $"Text exceeds {maxBytes} UTF-8 bytes.");
        }
        catch (EncoderFallbackException) { throw TextError(field, "Text contains invalid UTF-16."); }
        int scalars = 0;
        foreach (Rune rune in text.EnumerateRunes())
        {
            if (Rune.IsControl(rune)) throw TextError(field, "Control characters are not supported in model text.");
            if (++scalars > maxScalars) throw TextError(field, $"Text exceeds {maxScalars} Unicode scalar values.");
        }
    }

    private static UiAuthoringException TextError(string field, string cause)
        => new("UI_MODEL", "<model>", 1, 1, field, cause);

    internal static void Validate(Transform2D value)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.ScaleX)
            || !float.IsFinite(value.ScaleY) || !float.IsFinite(value.Rotation) || !float.IsFinite(value.Shear)
            || value.ScaleX <= 0 || value.ScaleY <= 0)
            throw new ArgumentOutOfRangeException(nameof(Transform2D), "Transforms require finite values and positive scales.");
    }

    internal static void Validate(Sprite2D value)
    {
        if (!float.IsFinite(value.Width) || !float.IsFinite(value.Height) || value.Width < 0 || value.Height < 0
            || !Unit(value.R) || !Unit(value.G) || !Unit(value.B) || !Unit(value.A)
            || (value.AssetKey is not null && string.IsNullOrWhiteSpace(value.AssetKey)))
            throw new ArgumentOutOfRangeException(nameof(Sprite2D), "Sprites require finite nonnegative extents, colors in [0, 1], and a nonempty optional asset key.");
    }

    private static bool Unit(float value) => float.IsFinite(value) && value is >= 0 and <= 1;
}
