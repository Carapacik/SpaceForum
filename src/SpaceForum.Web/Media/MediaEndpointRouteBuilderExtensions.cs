using System.Globalization;
using System.Net;
using Amazon.S3;
using Microsoft.AspNetCore.Antiforgery;
using Npgsql;
using SpaceForum.Web.Security;

namespace SpaceForum.Web.Media;

public static class MediaEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapSpaceForumMedia(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/media/{attachmentId:guid}", ServeAttachmentAsync);
        endpoints.MapGet("/media/avatars/{login}", ServeAvatarAsync);
        endpoints.MapPost("/actions/attachments/upload", UploadAttachmentAsync)
            .RequireAuthorization();
        endpoints.MapPost("/actions/profile/avatar", UploadAvatarAsync)
            .RequireAuthorization();
        return endpoints;
    }

    private static async Task ServeAttachmentAsync(
        Guid attachmentId,
        NpgsqlDataSource dataSource,
        S3MediaStorage storage,
        HttpContext context)
    {
        await using var command = dataSource.CreateCommand(
            "SELECT \"StorageKey\",\"ContentType\",\"OriginalFileName\",\"Size\" " +
            "FROM discussions.attachments WHERE \"Id\"=@id;");
        command.Parameters.AddWithValue("id", attachmentId);
        await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
        if (!await reader.ReadAsync(context.RequestAborted))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var item = new StoredMedia(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt64(3));
        await WriteObjectAsync(item, storage, context, cacheControl: "public,max-age=31536000,immutable");
    }

    private static async Task ServeAvatarAsync(
        string login,
        NpgsqlDataSource dataSource,
        S3MediaStorage storage,
        HttpContext context)
    {
        await using var command = dataSource.CreateCommand(
            "SELECT attachment.\"StorageKey\",attachment.\"ContentType\"," +
            "attachment.\"OriginalFileName\",attachment.\"Size\" " +
            "FROM discussions.attachments AS attachment " +
            "INNER JOIN members.member_profiles AS profile " +
            "ON profile.\"Id\"=attachment.\"UploaderMemberId\" " +
            "WHERE profile.\"Login\"=@login " +
            "AND attachment.\"StorageKey\"='avatars/'||profile.\"Login\";");
        command.Parameters.AddWithValue("login", login);
        await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
        if (await reader.ReadAsync(context.RequestAborted))
        {
            var item = new StoredMedia(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt64(3));
            await WriteObjectAsync(item, storage, context, cacheControl: "public,max-age=60");
            return;
        }

        var initial = WebUtility.HtmlEncode(login[..Math.Min(2, login.Length)].ToUpperInvariant());
        var svg = $"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 96 96'><rect width='96' height='96' rx='48' fill='#6d28d9'/><text x='48' y='58' text-anchor='middle' font-family='sans-serif' font-size='32' font-weight='700' fill='white'>{initial}</text></svg>";
        context.Response.ContentType = "image/svg+xml; charset=utf-8";
        context.Response.Headers.CacheControl = "public,max-age=300";
        context.Response.Headers.XContentTypeOptions = "nosniff";
        await context.Response.WriteAsync(svg, context.RequestAborted);
    }

    private static async Task<IResult> UploadAttachmentAsync(
        NpgsqlDataSource dataSource,
        S3MediaStorage storage,
        MediaUploadProcessor processor,
        IAntiforgery antiforgery,
        TimeProvider timeProvider,
        HttpContext context)
    {
        var memberId = context.User.GetMemberId();
        if (memberId is null)
        {
            return Results.Unauthorized();
        }

        await antiforgery.ValidateRequestAsync(context);
        var form = await context.Request.ReadFormAsync(context.RequestAborted);
        var file = form.Files.GetFile("file");
        if (file is null)
        {
            return InvalidMedia();
        }

        await using var media = await processor.PrepareAttachmentAsync(file, context.RequestAborted);
        if (media is null)
        {
            return InvalidMedia();
        }

        var id = Guid.CreateVersion7(timeProvider.GetUtcNow());
        var storageKey = $"attachments/{id:N}/{media.FileName}";
        await storage.PutAsync(
            storageKey,
            media.Content,
            media.ContentLength,
            media.ContentType,
            context.RequestAborted);

        try
        {
            await InsertAttachmentAsync(
                dataSource,
                id,
                memberId.Value,
                media.FileName,
                storageKey,
                media.ContentType,
                media.ContentLength,
                timeProvider.GetUtcNow(),
                context.RequestAborted);
        }
        catch
        {
            await storage.DeleteAsync(storageKey, context.RequestAborted);
            throw;
        }

        return Results.Ok(new
        {
            Url = $"/media/{id}",
            Name = media.FileName,
            media.IsImage,
            media.IsVideo,
        });
    }

    private static async Task<IResult> UploadAvatarAsync(
        NpgsqlDataSource dataSource,
        S3MediaStorage storage,
        MediaUploadProcessor processor,
        IAntiforgery antiforgery,
        TimeProvider timeProvider,
        HttpContext context)
    {
        var memberId = context.User.GetMemberId();
        if (memberId is null)
        {
            return Results.Unauthorized();
        }

        await antiforgery.ValidateRequestAsync(context);
        var form = await context.Request.ReadFormAsync(context.RequestAborted);
        var file = form.Files.GetFile("avatar");
        if (file is null)
        {
            return Results.Redirect("/account/manage?avatar=invalid");
        }

        await using var media = await processor.PrepareAvatarAsync(file, context.RequestAborted);
        if (media is null)
        {
            return Results.Redirect("/account/manage?avatar=invalid");
        }

        await using var lookup = dataSource.CreateCommand(
            "SELECT \"Login\" FROM members.member_profiles WHERE \"Id\"=@memberId;");
        lookup.Parameters.AddWithValue("memberId", memberId.Value);
        var login = (string?)await lookup.ExecuteScalarAsync(context.RequestAborted);
        if (login is null)
        {
            return Results.NotFound();
        }

        var storageKey = $"avatars/{login}";
        await storage.PutAsync(
            storageKey,
            media.Content,
            media.ContentLength,
            media.ContentType,
            context.RequestAborted);

        await using var command = dataSource.CreateCommand(
            "INSERT INTO discussions.attachments " +
            "(\"Id\",\"UploaderMemberId\",\"OriginalFileName\",\"StorageKey\",\"ContentType\",\"Size\",\"CreatedAt\") " +
            "VALUES (@id,@memberId,@name,@key,@type,@size,@createdAt) " +
            "ON CONFLICT (\"StorageKey\") DO UPDATE SET " +
            "\"OriginalFileName\"=EXCLUDED.\"OriginalFileName\"," +
            "\"ContentType\"=EXCLUDED.\"ContentType\",\"Size\"=EXCLUDED.\"Size\"," +
            "\"CreatedAt\"=EXCLUDED.\"CreatedAt\";");
        command.Parameters.AddWithValue("id", Guid.CreateVersion7(timeProvider.GetUtcNow()));
        command.Parameters.AddWithValue("memberId", memberId.Value);
        command.Parameters.AddWithValue("name", media.FileName);
        command.Parameters.AddWithValue("key", storageKey);
        command.Parameters.AddWithValue("type", media.ContentType);
        command.Parameters.AddWithValue("size", media.ContentLength);
        command.Parameters.AddWithValue("createdAt", timeProvider.GetUtcNow());
        await command.ExecuteNonQueryAsync(context.RequestAborted);
        return Results.Redirect("/account/manage?avatar=updated");
    }

    private static async Task InsertAttachmentAsync(
        NpgsqlDataSource dataSource,
        Guid id,
        Guid memberId,
        string fileName,
        string storageKey,
        string contentType,
        long size,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "INSERT INTO discussions.attachments " +
            "(\"Id\",\"UploaderMemberId\",\"OriginalFileName\",\"StorageKey\",\"ContentType\",\"Size\",\"CreatedAt\") " +
            "VALUES (@id,@memberId,@name,@key,@type,@size,@createdAt);");
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("memberId", memberId);
        command.Parameters.AddWithValue("name", fileName);
        command.Parameters.AddWithValue("key", storageKey);
        command.Parameters.AddWithValue("type", contentType);
        command.Parameters.AddWithValue("size", size);
        command.Parameters.AddWithValue("createdAt", createdAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task WriteObjectAsync(
        StoredMedia item,
        S3MediaStorage storage,
        HttpContext context,
        string cacheControl)
    {
        var range = ParseRange(context.Request.Headers.Range.ToString(), item.Size);
        if (range is { IsValid: false })
        {
            context.Response.StatusCode = StatusCodes.Status416RangeNotSatisfiable;
            context.Response.Headers.ContentRange = $"bytes */{item.Size.ToString(CultureInfo.InvariantCulture)}";
            return;
        }

        var start = range?.Start;
        var end = range?.End;
        try
        {
            using var response = await storage.OpenReadAsync(item.StorageKey, start, end, context.RequestAborted);
            context.Response.StatusCode = range is null
                ? StatusCodes.Status200OK
                : StatusCodes.Status206PartialContent;
            context.Response.ContentType = item.ContentType;
            context.Response.Headers.AcceptRanges = "bytes";
            context.Response.Headers.CacheControl = cacheControl;
            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers.ContentDisposition =
                $"inline; filename*=UTF-8''{Uri.EscapeDataString(item.FileName)}";
            context.Response.ContentLength = response.ContentLength;
            if (range is not null)
            {
                context.Response.Headers.ContentRange =
                    $"bytes {start}-{end}/{item.Size.ToString(CultureInfo.InvariantCulture)}";
            }

            await response.ResponseStream.CopyToAsync(context.Response.Body, context.RequestAborted);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode is HttpStatusCode.NotFound)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
        }
    }

    private static RequestedRange? ParseRange(string value, long size)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!value.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase)
            || value.Contains(',', StringComparison.Ordinal))
        {
            return RequestedRange.Invalid;
        }

        var parts = value[6..].Split('-', 2);
        if (parts.Length != 2)
        {
            return RequestedRange.Invalid;
        }

        if (string.IsNullOrEmpty(parts[0]))
        {
            if (!long.TryParse(parts[1], CultureInfo.InvariantCulture, out var suffixLength)
                || suffixLength <= 0)
            {
                return RequestedRange.Invalid;
            }

            var start = Math.Max(0, size - suffixLength);
            return new RequestedRange(start, size - 1, true);
        }

        if (!long.TryParse(parts[0], CultureInfo.InvariantCulture, out var requestedStart)
            || requestedStart < 0
            || requestedStart >= size)
        {
            return RequestedRange.Invalid;
        }

        var requestedEnd = size - 1;
        if (!string.IsNullOrEmpty(parts[1])
            && (!long.TryParse(parts[1], CultureInfo.InvariantCulture, out requestedEnd)
                || requestedEnd < requestedStart))
        {
            return RequestedRange.Invalid;
        }

        return new RequestedRange(requestedStart, Math.Min(requestedEnd, size - 1), true);
    }

    private static IResult InvalidMedia() => Results.Json(
        new { Error = "Only valid JPEG, PNG, WebP, MP4, or WebM media is accepted." },
        statusCode: StatusCodes.Status415UnsupportedMediaType);

    private sealed record StoredMedia(string StorageKey, string ContentType, string FileName, long Size);

    private sealed record RequestedRange(long Start, long End, bool IsValid)
    {
        public static RequestedRange Invalid { get; } = new(0, 0, false);
    }
}
