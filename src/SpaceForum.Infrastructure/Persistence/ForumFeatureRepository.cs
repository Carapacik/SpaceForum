using Npgsql;
using SpaceForum.Application.Forums;
using SpaceForum.Domain.Discussions;

namespace SpaceForum.Infrastructure.Persistence;

public sealed class ForumFeatureRepository(NpgsqlDataSource dataSource) : IForumFeatureRepository
{
    public async Task<PostAccess?> GetPostAccessAsync(Guid postId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT \"Id\", \"TopicId\", \"AuthorId\", \"Number\", \"HiddenAt\" IS NOT NULL FROM discussions.posts WHERE \"Id\" = @postId;");
        command.Parameters.AddWithValue("postId", postId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetInt32(3), reader.GetBoolean(4)) : null;
    }

    public async Task<bool> EditPostAsync(Guid postId, Guid editorId, string body, DateTimeOffset editedAt, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            WITH current_post AS (
                SELECT "Body" FROM discussions.posts WHERE "Id" = @postId AND "HiddenAt" IS NULL FOR UPDATE
            ), revision AS (
                INSERT INTO discussions.post_revisions ("Id", "PostId", "EditorMemberId", "PreviousBody", "Body", "EditedAt")
                SELECT @revisionId, @postId, @editorId, "Body", @body, @editedAt
                FROM current_post WHERE "Body" <> @body
                RETURNING "PostId"
            )
            UPDATE discussions.posts
            SET "Body" = @body, "UpdatedAt" = @editedAt, "Version" = "Version" + 1
            WHERE "Id" IN (SELECT "PostId" FROM revision);
            """;
        command.Parameters.AddWithValue("revisionId", Guid.CreateVersion7(editedAt));
        command.Parameters.AddWithValue("postId", postId);
        command.Parameters.AddWithValue("editorId", editorId);
        command.Parameters.AddWithValue("body", body);
        command.Parameters.AddWithValue("editedAt", editedAt);
        var updated = await command.ExecuteNonQueryAsync(cancellationToken) == 1;
        await transaction.CommitAsync(cancellationToken);
        return updated;
    }

    public async Task<IReadOnlyList<PostRevisionView>> GetPostRevisionsAsync(Guid postId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT revision."Id", revision."PreviousBody", revision."Body", editor."Login", revision."EditedAt"
            FROM discussions.post_revisions AS revision
            INNER JOIN members.member_profiles AS editor ON editor."Id" = revision."EditorMemberId"
            WHERE revision."PostId" = @postId ORDER BY revision."EditedAt" DESC;
            """);
        command.Parameters.AddWithValue("postId", postId);
        var results = new List<PostRevisionView>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) results.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetFieldValue<DateTimeOffset>(4)));
        return results;
    }

    public async Task<bool> SetPostHiddenAsync(Guid postId, Guid actorId, bool hidden, DateTimeOffset changedAt, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "UPDATE discussions.posts SET \"HiddenAt\" = @hiddenAt, \"HiddenByMemberId\" = @hiddenBy, \"UpdatedAt\" = @changedAt, \"Version\" = \"Version\" + 1 WHERE \"Id\" = @postId AND \"Number\" > 1;");
        command.Parameters.AddWithValue("postId", postId);
        command.Parameters.AddWithValue("changedAt", changedAt);
        command.Parameters.AddWithValue("hiddenAt", hidden ? changedAt : DBNull.Value);
        command.Parameters.AddWithValue("hiddenBy", hidden ? actorId : DBNull.Value);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<string?> SetSubscriptionAsync(Guid topicId, Guid memberId, string? state, int lastReadPostNumber, DateTimeOffset changedAt, CancellationToken cancellationToken)
    {
        if (state is null)
        {
            await using var delete = dataSource.CreateCommand("DELETE FROM discussions.topic_subscriptions WHERE \"TopicId\" = @topicId AND \"MemberId\" = @memberId;");
            delete.Parameters.AddWithValue("topicId", topicId);
            delete.Parameters.AddWithValue("memberId", memberId);
            await delete.ExecuteNonQueryAsync(cancellationToken);
            return null;
        }

        await using var command = dataSource.CreateCommand(
            """
            INSERT INTO discussions.topic_subscriptions ("TopicId", "MemberId", "State", "LastReadPostNumber", "UpdatedAt")
            VALUES (@topicId, @memberId, @state, @lastReadPostNumber, @changedAt)
            ON CONFLICT ("TopicId", "MemberId") DO UPDATE
            SET "State" = EXCLUDED."State", "LastReadPostNumber" = GREATEST(topic_subscriptions."LastReadPostNumber", EXCLUDED."LastReadPostNumber"), "UpdatedAt" = EXCLUDED."UpdatedAt"
            RETURNING "State";
            """);
        command.Parameters.AddWithValue("topicId", topicId);
        command.Parameters.AddWithValue("memberId", memberId);
        command.Parameters.AddWithValue("state", state);
        command.Parameters.AddWithValue("lastReadPostNumber", lastReadPostNumber);
        command.Parameters.AddWithValue("changedAt", changedAt);
        return (string?)await command.ExecuteScalarAsync(cancellationToken);
    }

    public async Task MarkReadAsync(Guid topicId, Guid memberId, int postNumber, DateTimeOffset changedAt, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            INSERT INTO discussions.topic_subscriptions ("TopicId", "MemberId", "State", "LastReadPostNumber", "UpdatedAt")
            VALUES (@topicId, @memberId, 'Following', @postNumber, @changedAt)
            ON CONFLICT ("TopicId", "MemberId") DO UPDATE
            SET "LastReadPostNumber" = GREATEST(topic_subscriptions."LastReadPostNumber", EXCLUDED."LastReadPostNumber"), "UpdatedAt" = EXCLUDED."UpdatedAt";
            """);
        command.Parameters.AddWithValue("topicId", topicId); command.Parameters.AddWithValue("memberId", memberId); command.Parameters.AddWithValue("postNumber", postNumber); command.Parameters.AddWithValue("changedAt", changedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<NotificationView>> GetNotificationsAsync(Guid memberId, int take, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT notification."Id", notification."Type", COALESCE(actor."DisplayName", 'SpaceForum'), topic."Number", topic."Slug",
                   post."Number", notification."CreatedAt", notification."ReadAt" IS NOT NULL
            FROM discussions.notifications AS notification
            LEFT JOIN members.member_profiles AS actor ON actor."Id" = notification."ActorMemberId"
            LEFT JOIN discussions.topics AS topic ON topic."Id" = notification."TopicId"
            LEFT JOIN discussions.posts AS post ON post."Id" = notification."PostId"
            WHERE notification."RecipientMemberId" = @memberId
            ORDER BY notification."CreatedAt" DESC LIMIT @take;
            """);
        command.Parameters.AddWithValue("memberId", memberId); command.Parameters.AddWithValue("take", take);
        var results = new List<NotificationView>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) results.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetInt64(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetInt32(5), reader.GetFieldValue<DateTimeOffset>(6), reader.GetBoolean(7)));
        return results;
    }

    public async Task<int> GetUnreadNotificationCountAsync(Guid memberId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT COUNT(*)::int FROM discussions.notifications WHERE \"RecipientMemberId\" = @memberId AND \"ReadAt\" IS NULL;"); command.Parameters.AddWithValue("memberId", memberId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
    }

    public async Task MarkNotificationsReadAsync(Guid memberId, DateTimeOffset readAt, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("UPDATE discussions.notifications SET \"ReadAt\" = @readAt WHERE \"RecipientMemberId\" = @memberId AND \"ReadAt\" IS NULL;"); command.Parameters.AddWithValue("memberId", memberId); command.Parameters.AddWithValue("readAt", readAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ReactionView>> GetReactionsAsync(Guid postId, Guid? actorId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT \"Reaction\", COUNT(*)::int, BOOL_OR(\"MemberId\" = @actorId) FROM discussions.post_reactions WHERE \"PostId\" = @postId GROUP BY \"Reaction\" ORDER BY \"Reaction\";");
        command.Parameters.AddWithValue("postId", postId); command.Parameters.AddWithValue("actorId", actorId ?? Guid.Empty);
        var results = new List<ReactionView>(); await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) results.Add(new(reader.GetString(0), reader.GetInt32(1), reader.GetBoolean(2))); return results;
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<ReactionView>>> GetTopicReactionsAsync(Guid topicId, Guid? actorId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT reaction."PostId", reaction."Reaction", COUNT(*)::int, BOOL_OR(reaction."MemberId" = @actorId)
            FROM discussions.post_reactions AS reaction
            INNER JOIN discussions.posts AS post ON post."Id" = reaction."PostId"
            WHERE post."TopicId" = @topicId
            GROUP BY reaction."PostId", reaction."Reaction" ORDER BY reaction."PostId", reaction."Reaction";
            """);
        command.Parameters.AddWithValue("topicId", topicId); command.Parameters.AddWithValue("actorId", actorId ?? Guid.Empty);
        var groups = new Dictionary<Guid, List<ReactionView>>(); await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) { var postId = reader.GetGuid(0); if (!groups.TryGetValue(postId, out var items)) groups[postId] = items = []; items.Add(new(reader.GetString(1), reader.GetInt32(2), reader.GetBoolean(3))); }
        return groups.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<ReactionView>)pair.Value);
    }

    public async Task<bool> ToggleReactionAsync(Guid postId, Guid memberId, string reaction, DateTimeOffset changedAt, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            WITH removed AS (
                DELETE FROM discussions.post_reactions WHERE "PostId" = @postId AND "MemberId" = @memberId AND "Reaction" = @reaction RETURNING 1
            )
            INSERT INTO discussions.post_reactions ("PostId", "MemberId", "Reaction", "CreatedAt")
            SELECT @postId, @memberId, @reaction, @changedAt
            WHERE NOT EXISTS (SELECT 1 FROM removed) AND EXISTS (SELECT 1 FROM discussions.posts WHERE "Id" = @postId)
            ON CONFLICT DO NOTHING;
            """);
        command.Parameters.AddWithValue("postId", postId); command.Parameters.AddWithValue("memberId", memberId); command.Parameters.AddWithValue("reaction", reaction); command.Parameters.AddWithValue("changedAt", changedAt);
        await command.ExecuteNonQueryAsync(cancellationToken); return true;
    }

    public async Task<bool> CreateReportAsync(Guid postId, Guid reporterId, string reason, string? details, DateTimeOffset createdAt, CancellationToken cancellationToken)
    {
        try
        {
            await using var command = dataSource.CreateCommand(
                """
                INSERT INTO discussions.post_reports ("Id", "PostId", "ReporterMemberId", "Reason", "Details", "Status", "CreatedAt", "UpdatedAt")
                SELECT @id, @postId, @reporterId, @reason, @details, 'Open', @createdAt, @createdAt
                WHERE EXISTS (
                    SELECT 1
                    FROM discussions.posts AS post
                    WHERE post."Id" = @postId AND post."AuthorId" <> @reporterId);
                """);
            command.Parameters.AddWithValue("id", Guid.CreateVersion7(createdAt)); command.Parameters.AddWithValue("postId", postId); command.Parameters.AddWithValue("reporterId", reporterId); command.Parameters.AddWithValue("reason", reason); command.Parameters.AddWithValue("details", details is null ? DBNull.Value : details); command.Parameters.AddWithValue("createdAt", createdAt);
            return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation) { return false; }
    }

    public async Task<IReadOnlyList<ReportView>> GetReportsAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT report."Id", report."PostId", topic."Number", topic."Slug", post."Number", reporter."Login", report."Reason", report."Details", report."Status", report."CreatedAt"
            FROM discussions.post_reports AS report
            INNER JOIN discussions.posts AS post ON post."Id" = report."PostId"
            INNER JOIN discussions.topics AS topic ON topic."Id" = post."TopicId"
            INNER JOIN members.member_profiles AS reporter ON reporter."Id" = report."ReporterMemberId"
            ORDER BY (report."Status" = 'Open') DESC, report."CreatedAt";
            """);
        var results = new List<ReportView>(); await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) results.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetInt64(2), reader.GetString(3), reader.GetInt32(4), reader.GetString(5), reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7), reader.GetString(8), reader.GetFieldValue<DateTimeOffset>(9))); return results;
    }

    public async Task<bool> SetReportStatusAsync(Guid reportId, Guid actorId, string status, DateTimeOffset changedAt, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("UPDATE discussions.post_reports SET \"Status\" = @status, \"AssignedMemberId\" = @actorId, \"UpdatedAt\" = @changedAt WHERE \"Id\" = @reportId;"); command.Parameters.AddWithValue("reportId", reportId); command.Parameters.AddWithValue("actorId", actorId); command.Parameters.AddWithValue("status", status); command.Parameters.AddWithValue("changedAt", changedAt);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> ToggleBookmarkAsync(Guid postId, Guid memberId, DateTimeOffset changedAt, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            WITH removed AS (DELETE FROM discussions.post_bookmarks WHERE "PostId" = @postId AND "MemberId" = @memberId RETURNING 1)
            INSERT INTO discussions.post_bookmarks ("PostId", "MemberId", "CreatedAt")
            SELECT @postId, @memberId, @changedAt WHERE NOT EXISTS (SELECT 1 FROM removed) AND EXISTS (SELECT 1 FROM discussions.posts WHERE "Id" = @postId)
            ON CONFLICT DO NOTHING;
            """); command.Parameters.AddWithValue("postId", postId); command.Parameters.AddWithValue("memberId", memberId); command.Parameters.AddWithValue("changedAt", changedAt);
        await command.ExecuteNonQueryAsync(cancellationToken); return true;
    }

    public async Task<IReadOnlyList<BookmarkView>> GetBookmarksAsync(Guid memberId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT post."Id", topic."Number", topic."Slug", post."Number", topic."Title", author."DisplayName", bookmark."CreatedAt"
            FROM discussions.post_bookmarks AS bookmark
            INNER JOIN discussions.posts AS post ON post."Id" = bookmark."PostId"
            INNER JOIN discussions.topics AS topic ON topic."Id" = post."TopicId"
            INNER JOIN members.member_profiles AS author ON author."Id" = post."AuthorId"
            WHERE bookmark."MemberId" = @memberId ORDER BY bookmark."CreatedAt" DESC;
            """); command.Parameters.AddWithValue("memberId", memberId); var results = new List<BookmarkView>(); await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) results.Add(new(reader.GetGuid(0), reader.GetInt64(1), reader.GetString(2), reader.GetInt32(3), reader.GetString(4), reader.GetString(5), reader.GetFieldValue<DateTimeOffset>(6))); return results;
    }

    public async Task<TopicFeatureState> GetTopicFeaturesAsync(Guid topicId, Guid? memberId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        bool sticky; string? subscription;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT topic.\"IsSticky\", subscription.\"State\" FROM discussions.topics AS topic LEFT JOIN discussions.topic_subscriptions AS subscription ON subscription.\"TopicId\" = topic.\"Id\" AND subscription.\"MemberId\" = @memberId WHERE topic.\"Id\" = @topicId;";
            command.Parameters.AddWithValue("topicId", topicId); command.Parameters.AddWithValue("memberId", memberId ?? Guid.Empty); await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return new(false, null, []); sticky = reader.GetBoolean(0); subscription = reader.IsDBNull(1) ? null : reader.GetString(1);
        }
        var tags = new List<TagView>(); await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT tag.\"Id\", tag.\"Name\", tag.\"Slug\", tag.\"Description\", tag.\"Color\", tag.\"Position\" FROM discussions.tags AS tag INNER JOIN discussions.topic_tags AS mapping ON mapping.\"TagId\" = tag.\"Id\" WHERE mapping.\"TopicId\" = @topicId ORDER BY tag.\"Position\", tag.\"Name\";"; command.Parameters.AddWithValue("topicId", topicId); await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) tags.Add(ReadTag(reader));
        }
        return new(sticky, subscription, tags);
    }

    public async Task<IReadOnlyList<TagView>> GetTagsAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT \"Id\", \"Name\", \"Slug\", \"Description\", \"Color\", \"Position\" FROM discussions.tags ORDER BY \"Position\", \"Name\";"); var tags = new List<TagView>(); await using var reader = await command.ExecuteReaderAsync(cancellationToken); while (await reader.ReadAsync(cancellationToken)) tags.Add(ReadTag(reader)); return tags;
    }

    public async Task<bool> CreateTagAsync(TagView tag, CancellationToken cancellationToken)
    {
        try { await using var command=dataSource.CreateCommand("INSERT INTO discussions.tags (\"Id\",\"Name\",\"Slug\",\"Description\",\"Color\",\"Position\") VALUES (@id,@name,@slug,@description,@color,@position);"); command.Parameters.AddWithValue("id",tag.Id);command.Parameters.AddWithValue("name",tag.Name);command.Parameters.AddWithValue("slug",tag.Slug);command.Parameters.AddWithValue("description",tag.Description);command.Parameters.AddWithValue("color",tag.Color);command.Parameters.AddWithValue("position",tag.Position);return await command.ExecuteNonQueryAsync(cancellationToken)==1; }
        catch(PostgresException exception) when(exception.SqlState==PostgresErrorCodes.UniqueViolation){return false;}
    }

    public async Task<bool> DeleteTagAsync(Guid tagId, CancellationToken cancellationToken)
    {
        await using var command=dataSource.CreateCommand("DELETE FROM discussions.tags WHERE \"Id\"=@tagId;");command.Parameters.AddWithValue("tagId",tagId);return await command.ExecuteNonQueryAsync(cancellationToken)==1;
    }

    public async Task<bool> UpdateTopicAsync(Guid topicId, string title, Guid categoryId, bool isSticky, IReadOnlyCollection<Guid> tagIds, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken); await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction; update.CommandText = "UPDATE discussions.topics SET \"Title\" = @title, \"Slug\" = @slug, \"CategoryId\" = @categoryId, \"IsSticky\" = @isSticky, \"Version\" = \"Version\" + 1 WHERE \"Id\" = @topicId;";
            update.Parameters.AddWithValue("topicId", topicId); update.Parameters.AddWithValue("title", title); update.Parameters.AddWithValue("slug", TopicSlug.Create(title)); update.Parameters.AddWithValue("categoryId", categoryId); update.Parameters.AddWithValue("isSticky", isSticky); if (await update.ExecuteNonQueryAsync(cancellationToken) != 1) return false;
        }
        await using (var delete = connection.CreateCommand()) { delete.Transaction = transaction; delete.CommandText = "DELETE FROM discussions.topic_tags WHERE \"TopicId\" = @topicId;"; delete.Parameters.AddWithValue("topicId", topicId); await delete.ExecuteNonQueryAsync(cancellationToken); }
        foreach (var tagId in tagIds.Distinct().Take(5)) { await using var insert = connection.CreateCommand(); insert.Transaction = transaction; insert.CommandText = "INSERT INTO discussions.topic_tags (\"TopicId\", \"TagId\") VALUES (@topicId, @tagId) ON CONFLICT DO NOTHING;"; insert.Parameters.AddWithValue("topicId", topicId); insert.Parameters.AddWithValue("tagId", tagId); await insert.ExecuteNonQueryAsync(cancellationToken); }
        await transaction.CommitAsync(cancellationToken); return true;
    }

    private static TagView ReadTag(NpgsqlDataReader reader) => new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetInt32(5));
}
