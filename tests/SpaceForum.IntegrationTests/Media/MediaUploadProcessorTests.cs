using Microsoft.AspNetCore.Http;
using SpaceForum.Web.Media;

namespace SpaceForum.IntegrationTests.Media;

public sealed class MediaUploadProcessorTests
{
    private readonly MediaUploadProcessor processor = new();

    [Fact]
    public async Task ValidatesImageBytesInsteadOfTrustingFileNameOrContentType()
    {
        var bytes = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
        var file = CreateFile(bytes, "pretends-to-be-text.txt", "text/plain");

        await using var media = await processor.PrepareAttachmentAsync(file, CancellationToken.None);

        Assert.NotNull(media);
        Assert.True(media.IsImage);
        Assert.False(media.IsVideo);
        Assert.Equal("image/png", media.ContentType);
        Assert.Equal("pretends-to-be-text.png", media.FileName);
        Assert.Equal(bytes.Length, media.ContentLength);
    }

    [Fact]
    public async Task RejectsDocumentRenamedAsImage()
    {
        var file = CreateFile("%PDF-1.7 not an image"u8.ToArray(), "photo.png", "image/png");

        var media = await processor.PrepareAttachmentAsync(file, CancellationToken.None);

        Assert.Null(media);
    }

    [Fact]
    public async Task AcceptsStructurallyRecognizableMp4WithoutChangingBytes()
    {
        byte[] bytes =
        [
            0, 0, 0, 16, (byte)'f', (byte)'t', (byte)'y', (byte)'p',
            (byte)'i', (byte)'s', (byte)'o', (byte)'m', 0, 0, 0, 0,
            0, 0, 0, 8, (byte)'m', (byte)'d', (byte)'a', (byte)'t',
        ];
        var file = CreateFile(bytes, "clip.bin", "application/octet-stream");

        await using var media = await processor.PrepareAttachmentAsync(file, CancellationToken.None);

        Assert.NotNull(media);
        Assert.True(media.IsVideo);
        Assert.Equal("video/mp4", media.ContentType);
        Assert.Equal("clip.mp4", media.FileName);
        Assert.Equal(bytes.Length, media.ContentLength);
    }

    [Fact]
    public async Task AvatarRejectsVideoEvenWhenContainerIsValid()
    {
        byte[] bytes =
        [
            0, 0, 0, 16, (byte)'f', (byte)'t', (byte)'y', (byte)'p',
            (byte)'i', (byte)'s', (byte)'o', (byte)'m', 0, 0, 0, 0,
            0, 0, 0, 8, (byte)'m', (byte)'d', (byte)'a', (byte)'t',
        ];
        var file = CreateFile(bytes, "avatar.mp4", "video/mp4");

        var media = await processor.PrepareAvatarAsync(file, CancellationToken.None);

        Assert.Null(media);
    }

    private static FormFile CreateFile(byte[] bytes, string fileName, string contentType)
    {
        var stream = new MemoryStream(bytes, writable: false);
        return new FormFile(stream, 0, bytes.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType,
        };
    }
}
