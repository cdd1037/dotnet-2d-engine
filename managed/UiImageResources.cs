namespace GameAuthoringLab;

internal sealed record UiImageReference(string Path, string File, int Line, int Column, string Field);
internal sealed record UiImageResource(string Path, ImageAsset Asset);
internal sealed record BoundUiDocument(string Rml, string Rcss, string Stylesheet,
    IReadOnlyList<UiImageReference> References, IReadOnlyList<UiImageResource> Images);

// Rml URLs have extra interpretation beyond filesystem paths. A deliberately small ASCII
// relative-path grammar avoids scheme, percent, fragment, query and escaping aliases.
internal static class UiImageResources
{
    internal const int MaximumImages = 32;
    internal const long MaximumEncodedBytes = 16 * 1024 * 1024;
    internal const long MaximumDecodedBytes = 64 * 1024 * 1024;

    internal static bool ValidPath(string value)
    {
        if (value.Length is < 1 or > 255 || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '/' and not '.' and not '_' and not '-')) return false;
        if (value.Split('/').Any(s => s is "" or "." or ".." || s.EndsWith('.'))) return false;
        string ext = Path.GetExtension(value);
        return ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase) || ext.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
    }

    internal static void Add(List<UiImageReference> references, UiImageReference value)
    {
        if (!ValidPath(value.Path)) throw Error(value, "Expected a relative ASCII BMP/PNG/JPEG path; URI syntax, aliases and traversal are unsupported.");
        if (references.Any(r => r.Path == value.Path)) return;
        if (references.Count == MaximumImages) throw Error(value, $"At most {MaximumImages} unique document images are supported.");
        references.Add(value);
    }

    internal static IReadOnlyList<UiImageResource> Read(AssetRoot assets, string documentPath, IReadOnlyList<UiImageReference> references)
    {
        var resources = new List<UiImageResource>(references.Count);
        long encoded = 0, decoded = 0;
        foreach (var reference in references)
        {
            try
            {
                var asset = ImageAsset.Read(assets, assets.Sibling(documentPath, reference.Path));
                encoded += asset.EncodedLength;
                decoded += asset.DecodedBytes;
                if (encoded > MaximumEncodedBytes || decoded > MaximumDecodedBytes)
                    throw Error(reference, "Document images exceed 16 MiB encoded or 64 MiB decoded RGBA aggregate budget.");
                resources.Add(new(reference.Path, asset));
            }
            catch (AssetException e) { throw new UiAuthoringException("UI_RESOURCE", reference.File, reference.Line, reference.Column, reference.Field, e.Message, e); }
        }
        return resources.AsReadOnly();
    }

    private static UiAuthoringException Error(UiImageReference reference, string cause) =>
        new("UI_RESOURCE", reference.File, reference.Line, reference.Column, reference.Field, cause);
}

// Own exact validated snapshots for the whole staged/live document lifetime, including
// images whose first use occurs only after a hidden/hover state becomes visible.
internal sealed class UiSourceStaging : IDisposable
{
    internal string DirectoryPath { get; }
    internal string DocumentPath => Path.Combine(DirectoryPath, "bound.rml");
    private UiSourceStaging(string path) => DirectoryPath = path;
    internal static UiSourceStaging Create(BoundUiDocument source)
    {
        var result = new UiSourceStaging(Path.Combine(Path.GetTempPath(), "gal-bound-ui-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(result.DirectoryPath);
        try
        {
            File.WriteAllText(result.DocumentPath, source.Rml, UiSourceFiles.StrictUtf8);
            File.WriteAllText(Path.Combine(result.DirectoryPath, source.Stylesheet), source.Rcss, UiSourceFiles.StrictUtf8);
            foreach (var image in source.Images)
            {
                string path = Path.Combine(result.DirectoryPath, image.Path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                file.Write(image.Asset.Bytes);
            }
            return result;
        }
        catch { result.Dispose(); throw; }
    }
    public void Dispose() { if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, true); }
}
