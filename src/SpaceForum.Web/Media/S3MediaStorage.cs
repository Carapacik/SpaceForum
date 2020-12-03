using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using Microsoft.Extensions.Options;

namespace SpaceForum.Web.Media;

public sealed class S3MediaStorage(IAmazonS3 client, IOptions<S3Options> options)
{
    private readonly string bucketName = options.Value.BucketName;

    public async Task EnsureBucketAsync(CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        for (var attempt = 1; attempt <= 30; attempt++)
        {
            try
            {
                if (!await AmazonS3Util.DoesS3BucketExistV2Async(client, bucketName))
                {
                    await client.PutBucketAsync(
                        new PutBucketRequest { BucketName = bucketName },
                        cancellationToken);
                }

                return;
            }
            catch (Exception exception) when (exception is AmazonS3Exception or HttpRequestException)
            {
                lastError = exception;
                if (attempt < 30)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                }
            }
        }

        throw new InvalidOperationException("The S3 media bucket could not be initialized.", lastError);
    }

    public Task PutAsync(
        string key,
        Stream content,
        long contentLength,
        string contentType,
        CancellationToken cancellationToken)
    {
        var request = new PutObjectRequest
        {
            BucketName = bucketName,
            Key = key,
            InputStream = content,
            ContentType = contentType,
            AutoCloseStream = false,
        };
        request.Headers.ContentLength = contentLength;
        return client.PutObjectAsync(request, cancellationToken);
    }

    public Task<GetObjectResponse> OpenReadAsync(
        string key,
        long? rangeStart,
        long? rangeEnd,
        CancellationToken cancellationToken)
    {
        var request = new GetObjectRequest
        {
            BucketName = bucketName,
            Key = key,
        };
        if (rangeStart is not null || rangeEnd is not null)
        {
            request.ByteRange = new ByteRange(
                rangeStart ?? 0,
                rangeEnd ?? throw new ArgumentException("A bounded S3 range requires an end position."));
        }

        return client.GetObjectAsync(request, cancellationToken);
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken) =>
        client.DeleteObjectAsync(bucketName, key, cancellationToken);
}
