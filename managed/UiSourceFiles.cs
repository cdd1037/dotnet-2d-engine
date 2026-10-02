using System.Text;
namespace GameAuthoringLab;

// Shared bounded UTF-8 snapshots. Each authoring profile still owns its grammar/allowlist.
internal static class UiSourceFiles
{
    internal const int MaxFileBytes = 64 * 1024;
    internal static readonly UTF8Encoding StrictUtf8 = new(false, true);
    internal static (byte[] Rml, byte[] Rcss, string RmlFile, string RcssFile) ReadAssetFiles(
        AssetRoot assets, string logicalPath, string stylesheet)
    {
        string file = logicalPath;
        try
        {
            file = assets.Resolve(logicalPath);
            string css = assets.Resolve(assets.Sibling(logicalPath, stylesheet));
            return (ReadBounded(file), ReadBounded(css), file, css);
        }
        catch (AssetException e)
        { throw Error("UI_FILE", file, 1, 1, "$", e.Message, e); }
    }

    internal static byte[] ReadBounded(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length is <= 0 or > MaxFileBytes) throw Error("UI_SIZE", path, 1, 1, "$", $"File must contain 1..{MaxFileBytes} bytes.");
            byte[] bytes = new byte[(int)stream.Length];
            stream.ReadExactly(bytes);
            if (stream.ReadByte() != -1) throw Error("UI_SIZE", path, 1, 1, "$", "File grew while being read.");
            return bytes;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { throw Error("UI_FILE", path, 1, 1, "$", e.Message, e); }
    }

    internal static string Decode(ReadOnlySpan<byte> bytes, string file)
    {
        if (bytes.Length is <= 0 or > MaxFileBytes) throw Error("UI_SIZE", file, 1, 1, "$", $"File must contain 1..{MaxFileBytes} bytes.");
        try
        {
            string value = StrictUtf8.GetString(bytes);
            return value.Length > 0 && value[0] == '\uFEFF' ? value[1..] : value;
        }
        catch (DecoderFallbackException e) { throw Error("UI_UTF8", file, 1, Math.Max(1, e.Index + 1), "$", "Invalid UTF-8 byte sequence.", e); }
    }

    private static UiAuthoringException Error(string code, string file, int line, int column, string field, string cause, Exception? inner = null)
        => new(code, file, line, column, field, cause, inner);
}
