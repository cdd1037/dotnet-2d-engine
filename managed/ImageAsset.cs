using System.Buffers.Binary;

namespace GameAuthoringLab;

/// <summary>
/// An owned encoded snapshot for synchronous UI staging. Only a read-only span escapes;
/// no decoder dependency, filesystem handle or mutable caller buffer is retained.
/// Limits bound encoded bytes and RGBA output, not native decoder temporaries.
/// Header preflight does not prove the pixel stream is decodable. The asset root is
/// authoring validation, not a sandbox against concurrent filesystem replacement.
/// </summary>
internal sealed class ImageAsset
{
    public const int MaximumEncodedBytes = 16 * 1024 * 1024;
    public const int MaximumDimension = 4096;
    public const long MaximumDecodedBytes = 64L * 1024 * 1024;
    private readonly byte[] _bytes;
    public ReadOnlySpan<byte> Bytes => _bytes;
    public int EncodedLength => _bytes.Length;
    public BitmapInfo Info { get; }
    public long DecodedBytes => (long)Info.Width * Info.Height * 4;

    private ImageAsset(byte[] bytes, BitmapInfo info) { _bytes = bytes; Info = info; }

    internal static ImageAsset Read(AssetRoot assets, string logicalPath)
    {
        ArgumentNullException.ThrowIfNull(assets);
        var format = FormatFor(assets, logicalPath);
        string path = assets.Resolve(logicalPath);
        try
        {
            using var stream = File.OpenRead(path);
            int length = CheckLength(stream, assets, logicalPath);
            byte[] bytes = new byte[length];
            stream.ReadExactly(bytes);
            if (stream.ReadByte() != -1)
                throw Error(assets, logicalPath, "ASSET_IMAGE_SIZE", "Image changed size while reading.");
            using var snapshot = new MemoryStream(bytes, writable: false);
            return new(bytes, ReadHeader(snapshot, length, format, path, assets, logicalPath));
        }
        catch (AssetException) { throw; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { throw Error(assets, logicalPath, "ASSET_IMAGE", e.Message, e); }
    }

    // World validation avoids an encoded whole-file allocation. UI staging uses Read
    // above so native decoding consumes exactly the immutable bytes just validated.
    internal static BitmapInfo ReadInfo(AssetRoot assets, string logicalPath)
    {
        ArgumentNullException.ThrowIfNull(assets);
        var format = FormatFor(assets, logicalPath);
        string path = assets.Resolve(logicalPath);
        try
        {
            using var stream = File.OpenRead(path);
            int length = CheckLength(stream, assets, logicalPath);
            return ReadHeader(stream, length, format, path, assets, logicalPath);
        }
        catch (AssetException) { throw; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { throw Error(assets, logicalPath, "ASSET_IMAGE", e.Message, e); }
    }

    private enum Format { Bmp, Png, Jpeg }

    private static Format FormatFor(AssetRoot assets, string logicalPath)
    {
        assets.ValidateLogicalPath(logicalPath);
        string extension = Path.GetExtension(logicalPath);
        if (extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase)) return Format.Bmp;
        if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase)) return Format.Png;
        if (extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)) return Format.Jpeg;
        throw Error(assets, logicalPath, "ASSET_IMAGE_FORMAT", "Expected a BMP, PNG or JPEG image (.bmp, .png, .jpg, .jpeg).");
    }

    private static int CheckLength(Stream stream, AssetRoot assets, string logicalPath)
    {
        long length = stream.Length;
        if (length is < 1 or > MaximumEncodedBytes)
            throw Error(assets, logicalPath, "ASSET_IMAGE_SIZE", "Expected 1..16777216 encoded image bytes.");
        return (int)length;
    }

    private static BitmapInfo ReadHeader(Stream stream, int length, Format format, string path, AssetRoot assets, string logicalPath)
    {
        Span<byte> header = stackalloc byte[54];
        long width, height;
        switch (format)
        {
            case Format.Bmp:
                stream.ReadExactly(header);
                if (header[0] != 'B' || header[1] != 'M') throw Signature();
                uint dibLength = BinaryPrimitives.ReadUInt32LittleEndian(header[14..]);
                if (dibLength < 40 || dibLength > length - 14)
                    throw Malformed("Expected a complete BMP DIB header of at least 40 bytes.");
                width = BinaryPrimitives.ReadInt32LittleEndian(header[18..]);
                // Top-down BMPs have a negative stored height. Widen before negation.
                height = Math.Abs((long)BinaryPrimitives.ReadInt32LittleEndian(header[22..]));
                break;
            case Format.Png:
                (width, height) = ReadPngHeader(stream, length, assets, logicalPath);
                break;
            default:
                stream.ReadExactly(header[..2]);
                if (header[0] != 0xff || header[1] != 0xd8) throw Signature();
                (width, height) = ReadJpegFrame(stream, length, assets, logicalPath);
                break;
        }
        if (width is < 1 or > MaximumDimension || height is < 1 or > MaximumDimension
            || width * height * 4 > MaximumDecodedBytes)
            throw Error(assets, logicalPath, "ASSET_IMAGE_SIZE", "Image dimensions must be 1..4096 and decoded RGBA at most 67108864 bytes.");
        return new(path, (int)width, (int)height);

        AssetException Signature() => Error(assets, logicalPath, "ASSET_IMAGE_FORMAT", "Image signature does not match its filename extension.");
        AssetException Malformed(string message) => Error(assets, logicalPath, "ASSET_IMAGE", message);
    }

    private static (long Width, long Height) ReadPngHeader(Stream stream, int length, AssetRoot assets, string logicalPath)
    {
        Span<byte> header = stackalloc byte[13];
        stream.ReadExactly(header[..8]);
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (!header[..8].SequenceEqual(signature))
            throw Error(assets, logicalPath, "ASSET_IMAGE_FORMAT", "PNG signature does not match its filename extension.");
        bool first = true, pixels = false;
        long width = 0, height = 0;
        while (stream.Position < length)
        {
            if (length - stream.Position < 12) throw Malformed("Truncated PNG chunk header or CRC.");
            stream.ReadExactly(header[..8]);
            uint chunkLength = BinaryPrimitives.ReadUInt32BigEndian(header);
            if (chunkLength > length - stream.Position - 4) throw Malformed("PNG chunk exceeds the encoded file.");
            bool ihdr = header.Slice(4, 4).SequenceEqual("IHDR"u8);
            bool idat = header.Slice(4, 4).SequenceEqual("IDAT"u8);
            bool iend = header.Slice(4, 4).SequenceEqual("IEND"u8);
            if (header.Slice(4, 4).SequenceEqual("acTL"u8) || header.Slice(4, 4).SequenceEqual("CgBI"u8))
                throw Malformed("Animated and vendor-specific PNG extensions are unsupported.");
            if (first != ihdr || (ihdr && chunkLength != 13))
                throw Malformed("PNG must start with one 13-byte IHDR chunk.");
            if (ihdr)
            {
                stream.ReadExactly(header);
                width = BinaryPrimitives.ReadUInt32BigEndian(header);
                height = BinaryPrimitives.ReadUInt32BigEndian(header[4..]);
                byte depth = header[8], color = header[9];
                bool validDepth = color switch
                {
                    0 or 3 => depth is 1 or 2 or 4 or 8,
                    2 or 4 or 6 => depth == 8,
                    _ => false
                };
                if (!validDepth || header[10] != 0 || header[11] != 0 || header[12] > 1)
                    throw Malformed("PNG requires valid <=8-bit depth/color, compression, filter and interlace values.");
            }
            else stream.Seek(chunkLength, SeekOrigin.Current);
            // CRC and compressed pixel integrity are deliberately decoder-owned.
            stream.Seek(4, SeekOrigin.Current);
            first = false;
            if (idat) pixels = true;
            if (iend)
            {
                if (chunkLength != 0 || !pixels || stream.Position != length)
                    throw Malformed("PNG requires IDAT and a final empty IEND with no trailing bytes.");
                return (width, height);
            }
        }
        throw Malformed("PNG is missing IDAT or IEND.");

        AssetException Malformed(string message) => Error(assets, logicalPath, "ASSET_IMAGE", message);
    }

    private static (int Width, int Height) ReadJpegFrame(Stream stream, int length, AssetRoot assets, string logicalPath)
    {
        Span<byte> segment = stackalloc byte[6];
        int width = 0, height = 0, frameComponents = 0;
        // Every iteration consumes a marker and its bounded payload; metadata cannot
        // induce an unbounded scan/allocation or seek past the known encoded budget.
        while (stream.Position < length)
        {
            if (stream.ReadByte() != 0xff) throw Malformed("Expected a JPEG marker before scan data.");
            int marker;
            do
            {
                if (stream.Position >= length) throw Malformed("Truncated JPEG marker.");
                marker = stream.ReadByte();
            } while (marker == 0xff);
            if (marker is < 0 or 0 or 0xd8 or 0xd9 || marker is >= 0xd0 and <= 0xd7)
                throw Malformed("JPEG frame/scan header is missing or has an invalid marker.");
            if (marker == 0x01) continue; // TEM has no length field.
            if (length - stream.Position < 2) throw Malformed("Truncated JPEG segment length.");
            stream.ReadExactly(segment[..2]);
            int segmentLength = BinaryPrimitives.ReadUInt16BigEndian(segment);
            if (segmentLength < 2 || segmentLength - 2 > length - stream.Position)
                throw Malformed("JPEG segment length is invalid or exceeds the encoded file.");
            bool frame = marker is >= 0xc0 and <= 0xcf && marker is not (0xc4 or 0xc8 or 0xcc);
            if (frame)
            {
                if (marker is not (0xc0 or 0xc1 or 0xc2) || frameComponents != 0 || segmentLength < 8)
                    throw Malformed("JPEG requires one baseline, extended sequential or progressive frame.");
                stream.ReadExactly(segment);
                frameComponents = segment[5];
                if (segment[0] != 8 || frameComponents is not (1 or 3 or 4) || segmentLength != 8 + 3 * frameComponents)
                    throw Malformed("JPEG requires 8-bit precision and 1, 3 or 4 components matching its frame length.");
                height = BinaryPrimitives.ReadUInt16BigEndian(segment[1..]);
                width = BinaryPrimitives.ReadUInt16BigEndian(segment[3..]);
                stream.Seek(segmentLength - 8, SeekOrigin.Current);
            }
            else if (marker == 0xda)
            {
                if (frameComponents == 0 || segmentLength < 6) throw Malformed("JPEG scan requires a preceding complete frame.");
                int components = stream.ReadByte();
                if (components < 1 || components > frameComponents || segmentLength != 6 + 2 * components)
                    throw Malformed("JPEG scan component count does not match its segment length.");
                // Subsequent scan/pixel data remains decoder-owned, including progressive scans.
                return (width, height);
            }
            else stream.Seek(segmentLength - 2, SeekOrigin.Current);
        }
        throw Malformed("JPEG has no complete frame and scan header.");

        AssetException Malformed(string message) => Error(assets, logicalPath, "ASSET_IMAGE", message);
    }

    private static AssetException Error(AssetRoot assets, string logicalPath, string code, string message, Exception? inner = null)
        => new(code, assets.DirectoryPath, logicalPath, message, inner);
}
