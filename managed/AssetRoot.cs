using System.Buffers.Binary;

namespace GameAuthoringLab;

/// <summary>Stable resource diagnostics; source adapters add their JSON/XML field location.</summary>
internal sealed class AssetException(string code, string root, string logicalPath, string cause, Exception? inner = null)
    : IOException($"{root} [{code}] {logicalPath}: {cause}", inner)
{
    public string Code { get; } = code;
    public string Root { get; } = root;
    public string LogicalPath { get; } = logicalPath;
}

/// <summary>
/// One captured filesystem root. Logical names are case-sensitive slash paths, never URIs.
/// The root/its ancestors are trusted; descendant links are rejected on each physical load.
/// This is authoring validation, not a sandbox against concurrent filesystem replacement.
/// </summary>
internal sealed class AssetRoot
{
    public string DirectoryPath { get; }

    public AssetRoot(string? directory = null)
    {
        string selected = directory ?? Environment.GetEnvironmentVariable("GAL_ASSET_ROOT")
            ?? (Directory.Exists(Path.Combine(AppContext.BaseDirectory, "assets"))
                ? Path.Combine(AppContext.BaseDirectory, "assets") : "assets");
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(selected);
            DirectoryPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(selected));
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or IOException)
        { throw new AssetException("ASSET_ROOT", selected, "$", e.Message, e); }
    }

    public string FilePath(string logicalPath)
    {
        ValidateLogicalPath(logicalPath);
        return Path.Combine(DirectoryPath, logicalPath.Replace('/', Path.DirectorySeparatorChar));
    }

    public string LogicalPathFor(string filePath)
    {
        try
        {
            string relative = Path.GetRelativePath(DirectoryPath, Path.GetFullPath(filePath))
                .Replace(Path.DirectorySeparatorChar, '/');
            ValidateLogicalPath(relative);
            return relative;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException)
        { throw Error("ASSET_PATH", filePath, "Expected a file within this asset root.", e); }
    }

    public string Resolve(string logicalPath)
    {
        ValidateLogicalPath(logicalPath);
        string resolved = DirectoryPath;
        try
        {
            string[] parts = logicalPath.Split('/');
            for (int i = 0; i < parts.Length; i++)
            {
                resolved = Path.Combine(resolved, parts[i]);
                FileAttributes attributes = File.GetAttributes(resolved);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw Error("ASSET_LINK", logicalPath, "Symbolic links/reparse points below the asset root are unsupported.");
                if (i == parts.Length - 1 && (attributes & FileAttributes.Directory) != 0)
                    throw Error("ASSET_FILE", logicalPath, "Expected a regular file, not a directory.");
            }
            return resolved;
        }
        catch (AssetException) { throw; }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        { throw Error("ASSET_MISSING", logicalPath, "Resource does not exist.", e); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { throw Error("ASSET_FILE", logicalPath, e.Message, e); }
    }

    public string ValidateBitmap(string logicalPath)
    {
        ValidateLogicalPath(logicalPath);
        if (!logicalPath.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase))
            throw Error("ASSET_BMP", logicalPath, "Only BMP texture resources are supported.");
        string file = Resolve(logicalPath);
        try
        {
            using var stream = File.OpenRead(file);
            Span<byte> header = stackalloc byte[54];
            stream.ReadExactly(header);
            int width = BinaryPrimitives.ReadInt32LittleEndian(header[18..]);
            int height = BinaryPrimitives.ReadInt32LittleEndian(header[22..]);
            if (header[0] != 'B' || header[1] != 'M' || width is < 1 or > 4096 || height is < 1 or > 4096)
                throw Error("ASSET_BMP", logicalPath, "Expected a 54-byte BMP header and dimensions 1..4096; native upload validates decoding.");
            return file;
        }
        catch (AssetException) { throw; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { throw Error("ASSET_BMP", logicalPath, e.Message, e); }
    }

    public string Sibling(string logicalPath, string sibling)
    {
        ValidateLogicalPath(logicalPath);
        ValidateLogicalPath(sibling);
        int slash = logicalPath.LastIndexOf('/');
        return slash < 0 ? sibling : logicalPath[..(slash + 1)] + sibling;
    }

    public void ValidateLogicalPath(string logicalPath)
    {
        if (string.IsNullOrWhiteSpace(logicalPath) || logicalPath.Length > 4096 || Path.IsPathRooted(logicalPath)
            || logicalPath.Any(c => char.IsControl(c) || c is '\\' or ':' or '<' or '>' or '"' or '|' or '?' or '*')
            || logicalPath.Split('/').Any(part => part is "" or "." or ".." || part.EndsWith(' ') || part.EndsWith('.')))
            throw Error("ASSET_PATH", logicalPath ?? "<null>", "Expected a relative '/' path, without URI, traversal, empty segments, controls or nonportable path characters.");
    }

    private AssetException Error(string code, string logicalPath, string cause, Exception? inner = null)
        => new(code, DirectoryPath, logicalPath, cause, inner);
}
