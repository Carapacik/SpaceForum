namespace SpaceForum.Web.Media;

public sealed class MediaUploadProcessor
{
    public const long MaximumImageBytes = 10 * 1024 * 1024;
    public const long MaximumVideoBytes = 15 * 1024 * 1024;

    public Task<PreparedMedia?> PrepareAttachmentAsync(IFormFile file, CancellationToken cancellationToken) =>
        PrepareAsync(file, avatar: false, cancellationToken);

    public Task<PreparedMedia?> PrepareAvatarAsync(IFormFile file, CancellationToken cancellationToken) =>
        PrepareAsync(file, avatar: true, cancellationToken);

    private static async Task<PreparedMedia?> PrepareAsync(
        IFormFile file,
        bool avatar,
        CancellationToken cancellationToken)
    {
        if (file.Length is <= 0 or > MaximumVideoBytes)
        {
            return null;
        }

        await using var source = file.OpenReadStream();
        var header = new byte[Math.Min(64 * 1024, checked((int)file.Length))];
        var headerLength = await ReadHeaderAsync(source, header, cancellationToken);
        var tail = new byte[Math.Min(16, checked((int)file.Length))];
        source.Position = file.Length - tail.Length;
        var tailLength = await ReadHeaderAsync(source, tail, cancellationToken);
        source.Position = 0;

        if (file.Length <= MaximumImageBytes)
        {
            var imageType = DetectImageType(
                header.AsSpan(0, headerLength),
                tail.AsSpan(0, tailLength),
                file.Length);
            if (imageType is not null)
            {
                var imageExtension = imageType switch
                {
                    "image/jpeg" => ".jpg",
                    "image/png" => ".png",
                    _ => ".webp",
                };
                return new PreparedMedia(
                    file.OpenReadStream(),
                    file.Length,
                    imageType,
                    NormalizeFileName(file.FileName, imageExtension),
                    isImage: true,
                    isVideo: false);
            }

            source.Position = 0;
        }

        if (avatar)
        {
            return null;
        }

        var videoType = DetectVideoType(header.AsSpan(0, headerLength));
        if (videoType is null)
        {
            return null;
        }

        var videoStream = file.OpenReadStream();
        var videoExtension = videoType == "video/mp4" ? ".mp4" : ".webm";
        return new PreparedMedia(
            videoStream,
            file.Length,
            videoType,
            NormalizeFileName(file.FileName, videoExtension),
            isImage: false,
            isVideo: true);
    }

    private static string? DetectImageType(ReadOnlySpan<byte> header, ReadOnlySpan<byte> tail, long length)
    {
        if (TryReadPngDimensions(header, tail, out var pngWidth, out var pngHeight)
            && HasSafeDimensions(pngWidth, pngHeight))
        {
            return "image/png";
        }

        if (TryReadJpegDimensions(header, tail, out var jpegWidth, out var jpegHeight)
            && HasSafeDimensions(jpegWidth, jpegHeight))
        {
            return "image/jpeg";
        }

        if (TryReadWebpDimensions(header, length, out var webpWidth, out var webpHeight)
            && HasSafeDimensions(webpWidth, webpHeight))
        {
            return "image/webp";
        }

        return null;
    }

    private static bool TryReadPngDimensions(
        ReadOnlySpan<byte> header,
        ReadOnlySpan<byte> tail,
        out int width,
        out int height)
    {
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        ReadOnlySpan<byte> endChunk = [0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82];
        if (header.Length < 24
            || !header.StartsWith(signature)
            || !header.Slice(12, 4).SequenceEqual("IHDR"u8)
            || !tail.EndsWith(endChunk))
        {
            width = 0;
            height = 0;
            return false;
        }

        var widthValue = ReadBigEndianUInt32(header[16..]);
        var heightValue = ReadBigEndianUInt32(header[20..]);
        if (widthValue > int.MaxValue || heightValue > int.MaxValue)
        {
            width = 0;
            height = 0;
            return false;
        }

        width = (int)widthValue;
        height = (int)heightValue;
        return true;
    }

    private static bool TryReadJpegDimensions(
        ReadOnlySpan<byte> header,
        ReadOnlySpan<byte> tail,
        out int width,
        out int height)
    {
        width = 0;
        height = 0;
        if (header.Length < 12
            || header[0] != 0xFF
            || header[1] != 0xD8
            || tail.Length < 2
            || tail[^2] != 0xFF
            || tail[^1] != 0xD9)
        {
            return false;
        }

        var offset = 2;
        while (offset + 4 < header.Length)
        {
            while (offset < header.Length && header[offset] != 0xFF)
            {
                offset++;
            }

            while (offset < header.Length && header[offset] == 0xFF)
            {
                offset++;
            }

            if (offset >= header.Length)
            {
                return false;
            }

            var marker = header[offset++];
            if (marker is 0xD8 or 0xD9 or 0x01 || marker is >= 0xD0 and <= 0xD7)
            {
                continue;
            }

            if (offset + 2 > header.Length)
            {
                return false;
            }

            var segmentLength = (header[offset] << 8) | header[offset + 1];
            if (segmentLength < 2 || offset + segmentLength > header.Length)
            {
                return false;
            }

            if (marker is 0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7 or 0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF)
            {
                if (segmentLength < 7)
                {
                    return false;
                }

                height = (header[offset + 3] << 8) | header[offset + 4];
                width = (header[offset + 5] << 8) | header[offset + 6];
                return true;
            }

            offset += segmentLength;
        }

        return false;
    }

    private static bool TryReadWebpDimensions(
        ReadOnlySpan<byte> header,
        long length,
        out int width,
        out int height)
    {
        width = 0;
        height = 0;
        if (header.Length < 30
            || !header[..4].SequenceEqual("RIFF"u8)
            || !header.Slice(8, 4).SequenceEqual("WEBP"u8)
            || ReadLittleEndianUInt32(header[4..]) + 8 != length)
        {
            return false;
        }

        var chunk = header.Slice(12, 4);
        if (chunk.SequenceEqual("VP8X"u8))
        {
            width = 1 + ReadLittleEndianUInt24(header[24..]);
            height = 1 + ReadLittleEndianUInt24(header[27..]);
            return true;
        }

        if (chunk.SequenceEqual("VP8L"u8) && header[20] == 0x2F)
        {
            width = 1 + header[21] + ((header[22] & 0x3F) << 8);
            height = 1 + (header[22] >> 6) + (header[23] << 2) + ((header[24] & 0x0F) << 10);
            return true;
        }

        if (chunk.SequenceEqual("VP8 "u8)
            && header[23] == 0x9D
            && header[24] == 0x01
            && header[25] == 0x2A)
        {
            width = (header[26] | (header[27] << 8)) & 0x3FFF;
            height = (header[28] | (header[29] << 8)) & 0x3FFF;
            return true;
        }

        return false;
    }

    private static bool HasSafeDimensions(int width, int height) =>
        width is > 0 and <= 12_000
        && height is > 0 and <= 12_000
        && (long)width * height <= 40_000_000;

    private static string? DetectVideoType(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 24
            && header[4] == (byte)'f'
            && header[5] == (byte)'t'
            && header[6] == (byte)'y'
            && header[7] == (byte)'p'
            && ReadBigEndianUInt32(header) is >= 16 and <= 65_536
            && header.IndexOf("mdat"u8) >= 8)
        {
            return "video/mp4";
        }

        ReadOnlySpan<byte> webmSignature = [0x1A, 0x45, 0xDF, 0xA3];
        ReadOnlySpan<byte> webmDocumentType = "webm"u8;
        ReadOnlySpan<byte> webmCluster = [0x1F, 0x43, 0xB6, 0x75];
        if (header.StartsWith(webmSignature)
            && header.IndexOf(webmDocumentType) >= 0
            && header.IndexOf(webmCluster) >= 0)
        {
            return "video/webm";
        }

        return null;
    }

    private static uint ReadBigEndianUInt32(ReadOnlySpan<byte> bytes) =>
        ((uint)bytes[0] << 24)
        | ((uint)bytes[1] << 16)
        | ((uint)bytes[2] << 8)
        | bytes[3];

    private static uint ReadLittleEndianUInt32(ReadOnlySpan<byte> bytes) =>
        bytes[0]
        | ((uint)bytes[1] << 8)
        | ((uint)bytes[2] << 16)
        | ((uint)bytes[3] << 24);

    private static int ReadLittleEndianUInt24(ReadOnlySpan<byte> bytes) =>
        bytes[0] | (bytes[1] << 8) | (bytes[2] << 16);

    private static async Task<int> ReadHeaderAsync(
        Stream source,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await source.ReadAsync(buffer[total..], cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    private static string NormalizeFileName(string originalFileName, string extension)
    {
        var baseName = Path.GetFileNameWithoutExtension(originalFileName).Trim();
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "media";
        }

        var safeName = new string(baseName
            .Where(character => !char.IsControl(character) && character is not '/' and not '\\')
            .Take(180)
            .ToArray());
        return $"{safeName}{extension}";
    }
}

public sealed class PreparedMedia(
    Stream content,
    long contentLength,
    string contentType,
    string fileName,
    bool isImage,
    bool isVideo) : IAsyncDisposable
{
    public Stream Content { get; } = content;

    public long ContentLength { get; } = contentLength;

    public string ContentType { get; } = contentType;

    public string FileName { get; } = fileName;

    public bool IsImage { get; } = isImage;

    public bool IsVideo { get; } = isVideo;

    public ValueTask DisposeAsync() => Content.DisposeAsync();
}
