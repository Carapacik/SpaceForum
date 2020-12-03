using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Options;
using SpaceForum.Web.Media;

namespace SpaceForum.IntegrationTests.Media;

public sealed class S3MediaStorageTests
{
    [Fact]
    public async Task StoresReadsRangesAndDeletesObjectWhenMinioIsConfigured()
    {
        var serviceUrl = Environment.GetEnvironmentVariable("SPACEFORUM_S3_TEST_URL");
        if (string.IsNullOrWhiteSpace(serviceUrl))
        {
            return;
        }

        var options = new S3Options
        {
            ServiceUrl = serviceUrl,
            AccessKey = "spaceforum",
            SecretKey = "spaceforum_s3_dev",
            BucketName = "spaceforum-media",
        };
        using var client = new AmazonS3Client(
            new BasicAWSCredentials(options.AccessKey, options.SecretKey),
            new AmazonS3Config
            {
                ServiceURL = options.ServiceUrl,
                ForcePathStyle = true,
                AuthenticationRegion = "us-east-1",
            });
        var storage = new S3MediaStorage(client, Options.Create(options));
        var cancellationToken = TestContext.Current.CancellationToken;
        await storage.EnsureBucketAsync(cancellationToken);

        var key = $"smoke-tests/{Guid.NewGuid():N}";
        var bytes = "SpaceForum S3 range check"u8.ToArray();
        try
        {
            await using (var upload = new MemoryStream(bytes, writable: false))
            {
                await storage.PutAsync(
                    key,
                    upload,
                    bytes.Length,
                    "application/octet-stream",
                    cancellationToken);
            }

            using var full = await storage.OpenReadAsync(key, null, null, cancellationToken);
            using var fullBuffer = new MemoryStream();
            await full.ResponseStream.CopyToAsync(fullBuffer, cancellationToken);
            Assert.Equal(bytes, fullBuffer.ToArray());

            using var range = await storage.OpenReadAsync(key, 11, 12, cancellationToken);
            using var rangeBuffer = new MemoryStream();
            await range.ResponseStream.CopyToAsync(rangeBuffer, cancellationToken);
            Assert.Equal("S3"u8.ToArray(), rangeBuffer.ToArray());
        }
        finally
        {
            await storage.DeleteAsync(key, cancellationToken);
        }
    }
}
