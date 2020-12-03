using Npgsql;
using NpgsqlTypes;
using SpaceForum.Application.Discussions;
using SpaceForum.Domain.Discussions;

namespace SpaceForum.Infrastructure.Persistence;

public sealed class DiscussionRepository(NpgsqlDataSource dataSource) : IDiscussionRepository
{
    private Topic? trackedTopic;

    public async Task<bool> CreateAsync(Topic topic, Post firstPost, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await InsertTopicAsync(connection, transaction, topic, cancellationToken);
            await InsertPostAsync(connection, transaction, firstPost, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (PostgresException exception) when (
            exception.SqlState is PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation)
        {
            return false;
        }
    }

    public async Task<Topic?> FindTopicAsync(
        Guid topicId,
        bool trackChanges,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT "Id", "CategoryId", "AuthorId", "Title", "CreatedAt", "LastActivityAt", "ReplyCount", "IsClosed", "Version"
            FROM discussions.topics
            WHERE "Id" = @topicId;
            """);
        command.Parameters.AddWithValue("topicId", topicId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        Topic? topic = null;
        if (await reader.ReadAsync(cancellationToken))
        {
            topic = Topic.Restore(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetString(3),
                reader.GetFieldValue<DateTimeOffset>(4),
                reader.GetFieldValue<DateTimeOffset>(5),
                reader.GetInt32(6),
                reader.GetBoolean(7),
                reader.GetInt64(8));
        }

        trackedTopic = trackChanges ? topic : null;
        return topic;
    }

    public async Task<int> GetNextPostNumberAsync(Guid topicId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "SELECT COALESCE(MAX(\"Number\"), 0) + 1 FROM discussions.posts WHERE \"TopicId\" = @topicId;");
        command.Parameters.AddWithValue("topicId", topicId);
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    public async Task<int> GetLastPostNumberAsync(Guid topicId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT COALESCE(MAX(\"Number\"), 0)::int FROM discussions.posts WHERE \"TopicId\" = @topicId;");
        command.Parameters.AddWithValue("topicId", topicId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
    }

    public async Task<bool> AddReplyAsync(Post post, CancellationToken cancellationToken)
    {
        var topic = trackedTopic;
        if (topic is null || topic.Id != post.TopicId)
        {
            throw new InvalidOperationException("The topic must be loaded for update before adding a reply.");
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using var updateTopic = connection.CreateCommand();
            updateTopic.Transaction = transaction;
            updateTopic.CommandText =
                """
                UPDATE discussions.topics
                SET "LastActivityAt" = @lastActivityAt,
                    "ReplyCount" = @replyCount,
                    "Version" = @version
                WHERE "Id" = @id AND "Version" = @expectedVersion AND NOT "IsClosed";
                """;
            updateTopic.Parameters.AddWithValue("lastActivityAt", topic.LastActivityAt);
            updateTopic.Parameters.AddWithValue("replyCount", topic.ReplyCount);
            updateTopic.Parameters.AddWithValue("version", topic.Version);
            updateTopic.Parameters.AddWithValue("id", topic.Id);
            updateTopic.Parameters.AddWithValue("expectedVersion", topic.Version - 1);
            if (await updateTopic.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }

            await InsertPostAsync(connection, transaction, post, cancellationToken);
            await InsertReplyNotificationsAsync(connection, transaction, post, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            trackedTopic = null;
            return true;
        }
        catch (PostgresException exception) when (
            exception.SqlState is PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation)
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<TopicListItem>> ListAsync(
        TopicSort sort,
        int take,
        CancellationToken cancellationToken)
    {
        var orderBy = sort switch
        {
            TopicSort.Newest => "topic.\"CreatedAt\" DESC, topic.\"Number\" DESC",
            TopicSort.Oldest => "topic.\"CreatedAt\" ASC, topic.\"Number\" ASC",
            TopicSort.Top => "topic.\"ReplyCount\" DESC, topic.\"LastActivityAt\" DESC",
            TopicSort.HighestRated => "\"Score\" DESC, topic.\"LastActivityAt\" DESC",
            TopicSort.LowestRated => "\"Score\" ASC, topic.\"LastActivityAt\" DESC",
            _ => "topic.\"LastActivityAt\" DESC, topic.\"Number\" DESC",
        };

        await using var command = dataSource.CreateCommand(
            $"""
            {TopicListSelect}
            ORDER BY {orderBy}
            LIMIT @take;
            """);
        command.Parameters.AddWithValue("take", take);
        return await ReadTopicListAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<TopicListItem>> ListByCategoryAsync(
        Guid categoryId,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"""
            {TopicListSelect}
            WHERE topic."CategoryId" = @categoryId
            ORDER BY topic."LastActivityAt" DESC;
            """);
        command.Parameters.AddWithValue("categoryId", categoryId);
        return await ReadTopicListAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<TopicListItem>> SearchAsync(
        string query,
        int take,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"""
            WITH search_query AS (
                SELECT websearch_to_tsquery('simple', @query) AS value
            )
            {TopicListSelect}
            CROSS JOIN search_query
            WHERE to_tsvector('simple', topic."Title") @@ search_query.value
               OR EXISTS (
                    SELECT 1
                    FROM discussions.posts AS matched_post
                    WHERE matched_post."TopicId" = topic."Id"
                      AND to_tsvector('simple', matched_post."Body") @@ search_query.value)
            ORDER BY topic."LastActivityAt" DESC
            LIMIT @take;
            """);
        command.Parameters.AddWithValue("query", query);
        command.Parameters.AddWithValue("take", take);
        return await ReadTopicListAsync(command, cancellationToken);
    }

    public async Task<TopicRoute?> GetRouteAsync(Guid topicId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "SELECT \"Number\", \"Slug\" FROM discussions.topics WHERE \"Id\" = @topicId;");
        command.Parameters.AddWithValue("topicId", topicId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new(reader.GetInt64(0), reader.GetString(1))
            : null;
    }

    public async Task<TopicDetail?> GetDetailAsync(
        long topicNumber,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var topicCommand = connection.CreateCommand();
        topicCommand.CommandText =
            """
            SELECT
                topic."Id",
                topic."Number",
                topic."Slug",
                topic."Title",
                topic."AuthorId",
                topic."CategoryId",
                category."Name",
                category."Slug",
                topic."IsClosed",
                COALESCE((SELECT SUM(vote."Value") FROM discussions.topic_votes AS vote WHERE vote."TopicId" = topic."Id"), 0)::int,
                (SELECT COUNT(*)::int FROM discussions.posts AS counted_post WHERE counted_post."TopicId" = topic."Id"),
                COALESCE((SELECT MAX(last_post."Number")::int FROM discussions.posts AS last_post WHERE last_post."TopicId" = topic."Id"), 0)
            FROM discussions.topics AS topic
            INNER JOIN forums.categories AS category ON category."Id" = topic."CategoryId"
            WHERE topic."Number" = @topicNumber;
            """;
        topicCommand.Parameters.AddWithValue("topicNumber", topicNumber);

        Guid id;
        string slug;
        string title;
        Guid authorId;
        Guid categoryId;
        string categoryName;
        string categorySlug;
        bool isClosed;
        int score;
        int totalPosts;
        int lastPostNumber;
        await using (var reader = await topicCommand.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            id = reader.GetGuid(0);
            slug = reader.GetString(2);
            title = reader.GetString(3);
            authorId = reader.GetGuid(4);
            categoryId = reader.GetGuid(5);
            categoryName = reader.GetString(6);
            categorySlug = reader.GetString(7);
            isClosed = reader.GetBoolean(8);
            score = reader.GetInt32(9);
            totalPosts = reader.GetInt32(10);
            lastPostNumber = reader.GetInt32(11);
        }

        pageSize = Math.Clamp(pageSize, 1, 100);
        var totalPages = Math.Max(1, (lastPostNumber + pageSize - 1) / pageSize);
        page = Math.Clamp(page, 1, totalPages);
        var firstPostNumber = ((page - 1) * pageSize) + 1;
        var finalPostNumber = page * pageSize;

        await using var postsCommand = connection.CreateCommand();
        postsCommand.CommandText =
            """
            SELECT
                post."Id",
                post."Number",
                post."Body",
                post."AuthorId",
                author."Login",
                author."DisplayName",
                post."CreatedAt",
                post."UpdatedAt",
                post."ReplyToPostId",
                replied."Number",
                post."HiddenAt" IS NOT NULL,
                (SELECT COUNT(*)::int FROM discussions.post_revisions AS revision WHERE revision."PostId" = post."Id")
            FROM discussions.posts AS post
            INNER JOIN members.member_profiles AS author ON author."Id" = post."AuthorId"
            LEFT JOIN discussions.posts AS replied ON replied."Id" = post."ReplyToPostId"
            WHERE post."TopicId" = @topicId
              AND post."Number" BETWEEN @firstPostNumber AND @finalPostNumber
            ORDER BY post."Number";
            """;
        postsCommand.Parameters.AddWithValue("topicId", id);
        postsCommand.Parameters.AddWithValue("firstPostNumber", firstPostNumber);
        postsCommand.Parameters.AddWithValue("finalPostNumber", finalPostNumber);
        var posts = new List<PostView>();
        await using (var reader = await postsCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                posts.Add(new(
                    reader.GetGuid(0),
                    reader.GetInt32(1),
                    reader.GetString(2),
                    reader.GetGuid(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetFieldValue<DateTimeOffset>(6),
                    reader.GetFieldValue<DateTimeOffset>(7),
                    reader.IsDBNull(8) ? null : reader.GetGuid(8),
                    reader.IsDBNull(9) ? null : reader.GetInt32(9),
                    reader.GetBoolean(10),
                    reader.GetInt32(11)));
            }
        }

        return new(
            id,
            topicNumber,
            slug,
            title,
            authorId,
            categoryId,
            categoryName,
            categorySlug,
            isClosed,
            score,
            page,
            pageSize,
            totalPosts,
            lastPostNumber,
            posts);
    }

    public async Task<bool> SetClosedAsync(
        Guid topicId,
        bool isClosed,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "UPDATE discussions.topics SET \"IsClosed\" = @isClosed, \"Version\" = \"Version\" + 1 WHERE \"Id\" = @topicId;");
        command.Parameters.AddWithValue("topicId", topicId);
        command.Parameters.AddWithValue("isClosed", isClosed);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> DeleteTopicAsync(Guid topicId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "DELETE FROM discussions.topics WHERE \"Id\" = @topicId;");
        command.Parameters.AddWithValue("topicId", topicId);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> DeleteReplyAsync(Guid postId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            DELETE FROM discussions.posts AS post
            WHERE post."Id" = @postId AND post."Number" > 1
            RETURNING "TopicId";
            """;
        command.Parameters.AddWithValue("postId", postId);
        var topicId = await command.ExecuteScalarAsync(cancellationToken);
        if (topicId is not Guid deletedTopicId)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await using var updateTopic = connection.CreateCommand();
        updateTopic.Transaction = transaction;
        updateTopic.CommandText =
            """
            UPDATE discussions.topics
            SET "ReplyCount" = (SELECT COUNT(*)::int - 1 FROM discussions.posts WHERE "TopicId" = @topicId),
                "LastActivityAt" = (SELECT MAX("CreatedAt") FROM discussions.posts WHERE "TopicId" = @topicId),
                "Version" = "Version" + 1
            WHERE "Id" = @topicId;
            """;
        updateTopic.Parameters.AddWithValue("topicId", deletedTopicId);
        await updateTopic.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<TopicVoteState> GetVoteAsync(
        Guid topicId,
        Guid? memberId,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT
                COALESCE((SELECT SUM("Value") FROM discussions.topic_votes WHERE "TopicId" = @topicId), 0)::int,
                (SELECT "Value" FROM discussions.topic_votes WHERE "TopicId" = @topicId AND "MemberId" = @memberId);
            """);
        command.Parameters.AddWithValue("topicId", topicId);
        var memberParameter = command.Parameters.Add("memberId", NpgsqlDbType.Uuid);
        memberParameter.Value = memberId.HasValue ? memberId.Value : DBNull.Value;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new(reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetInt16(1));
    }

    public async Task<TopicVoteState?> SetVoteAsync(
        Guid topicId,
        Guid memberId,
        int value,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using var remove = connection.CreateCommand();
            remove.Transaction = transaction;
            remove.CommandText =
                "DELETE FROM discussions.topic_votes WHERE \"TopicId\" = @topicId AND \"MemberId\" = @memberId AND \"Value\" = @value;";
            remove.Parameters.AddWithValue("topicId", topicId);
            remove.Parameters.AddWithValue("memberId", memberId);
            remove.Parameters.AddWithValue("value", (short)value);
            var removed = await remove.ExecuteNonQueryAsync(cancellationToken) == 1;

            if (!removed)
            {
                await using var upsert = connection.CreateCommand();
                upsert.Transaction = transaction;
                upsert.CommandText =
                    """
                    INSERT INTO discussions.topic_votes ("TopicId", "MemberId", "Value", "CreatedAt", "UpdatedAt")
                    VALUES (@topicId, @memberId, @value, @changedAt, @changedAt)
                    ON CONFLICT ("TopicId", "MemberId") DO UPDATE
                    SET "Value" = EXCLUDED."Value", "UpdatedAt" = EXCLUDED."UpdatedAt";
                    """;
                upsert.Parameters.AddWithValue("topicId", topicId);
                upsert.Parameters.AddWithValue("memberId", memberId);
                upsert.Parameters.AddWithValue("value", (short)value);
                upsert.Parameters.AddWithValue("changedAt", changedAt);
                await upsert.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return await GetVoteAsync(topicId, memberId, cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            return null;
        }
    }

    private static async Task<IReadOnlyList<TopicListItem>> ReadTopicListAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        var topics = new List<TopicListItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            topics.Add(new(
                reader.GetGuid(0),
                reader.GetInt64(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetInt32(8),
                reader.GetInt32(9),
                reader.GetFieldValue<DateTimeOffset>(10)));
        }

        return topics;
    }

    private static async Task InsertTopicAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Topic topic,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO discussions.topics
                ("Id", "CategoryId", "AuthorId", "Slug", "Title", "CreatedAt", "LastActivityAt", "ReplyCount", "IsClosed", "Version")
            VALUES
                (@id, @categoryId, @authorId, @slug, @title, @createdAt, @lastActivityAt, @replyCount, @isClosed, @version);
            """;
        command.Parameters.AddWithValue("id", topic.Id);
        command.Parameters.AddWithValue("categoryId", topic.CategoryId);
        command.Parameters.AddWithValue("authorId", topic.AuthorId);
        command.Parameters.AddWithValue("slug", TopicSlug.Create(topic.Title));
        command.Parameters.AddWithValue("title", topic.Title);
        command.Parameters.AddWithValue("createdAt", topic.CreatedAt);
        command.Parameters.AddWithValue("lastActivityAt", topic.LastActivityAt);
        command.Parameters.AddWithValue("replyCount", topic.ReplyCount);
        command.Parameters.AddWithValue("isClosed", topic.IsClosed);
        command.Parameters.AddWithValue("version", topic.Version);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertPostAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Post post,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO discussions.posts
                ("Id", "TopicId", "AuthorId", "Number", "Body", "ReplyToPostId", "CreatedAt", "UpdatedAt", "Version")
            VALUES
                (@id, @topicId, @authorId, @number, @body, @replyToPostId, @createdAt, @updatedAt, @version);
            """;
        command.Parameters.AddWithValue("id", post.Id);
        command.Parameters.AddWithValue("topicId", post.TopicId);
        command.Parameters.AddWithValue("authorId", post.AuthorId);
        command.Parameters.AddWithValue("number", post.Number);
        command.Parameters.AddWithValue("body", post.Body);
        var replyToParameter = command.Parameters.Add("replyToPostId", NpgsqlDbType.Uuid);
        replyToParameter.Value = post.ReplyToPostId.HasValue ? post.ReplyToPostId.Value : DBNull.Value;
        command.Parameters.AddWithValue("createdAt", post.CreatedAt);
        command.Parameters.AddWithValue("updatedAt", post.UpdatedAt);
        command.Parameters.AddWithValue("version", post.Version);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private const string TopicListSelect =
        """
        SELECT
            topic."Id",
            topic."Number",
            topic."Slug",
            topic."Title",
            category."Name",
            category."Slug",
            author."Login",
            author."DisplayName",
            topic."ReplyCount",
            COALESCE((SELECT SUM(vote."Value") FROM discussions.topic_votes AS vote WHERE vote."TopicId" = topic."Id"), 0)::int AS "Score",
            topic."LastActivityAt"
        FROM discussions.topics AS topic
        INNER JOIN forums.categories AS category ON category."Id" = topic."CategoryId"
        INNER JOIN members.member_profiles AS author ON author."Id" = topic."AuthorId"
        """;

    private static async Task InsertReplyNotificationsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Post post,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            WITH candidates AS (
                SELECT topic."AuthorId" AS "MemberId", 'NewReply'::varchar(40) AS "Type", 2 AS priority
                FROM discussions.topics AS topic
                WHERE topic."Id" = @topicId
                UNION ALL
                SELECT subscription."MemberId", 'NewReply', 2
                FROM discussions.topic_subscriptions AS subscription
                WHERE subscription."TopicId" = @topicId AND subscription."State" = 'Following'
                UNION ALL
                SELECT replied."AuthorId", 'PostReply', 1
                FROM discussions.posts AS replied
                WHERE replied."Id" = @replyToPostId
                UNION ALL
                SELECT profile."Id", 'Mention', 0
                FROM members.member_profiles AS profile
                WHERE position('@' || profile."Login" IN lower(@body)) > 0
            ), recipients AS (
                SELECT DISTINCT ON ("MemberId") "MemberId", "Type"
                FROM candidates
                WHERE "MemberId" <> @actorId
                ORDER BY "MemberId", priority
            )
            INSERT INTO discussions.notifications
                ("Id", "RecipientMemberId", "ActorMemberId", "Type", "TopicId", "PostId", "Data", "CreatedAt")
            SELECT gen_random_uuid(), recipient."MemberId", @actorId, recipient."Type", @topicId, @postId,
                   jsonb_build_object('postNumber', @postNumber), @createdAt
            FROM recipients AS recipient;

            UPDATE discussions.topic_subscriptions
            SET "LastReadPostNumber" = GREATEST("LastReadPostNumber", @postNumber),
                "UpdatedAt" = @createdAt
            WHERE "TopicId" = @topicId AND "MemberId" = @actorId;
            """;
        command.Parameters.AddWithValue("topicId", post.TopicId);
        command.Parameters.AddWithValue("postId", post.Id);
        command.Parameters.AddWithValue("actorId", post.AuthorId);
        command.Parameters.AddWithValue("postNumber", post.Number);
        command.Parameters.AddWithValue("body", post.Body.ToLowerInvariant());
        command.Parameters.AddWithValue("createdAt", post.CreatedAt);
        var replyToParameter = command.Parameters.Add("replyToPostId", NpgsqlDbType.Uuid);
        replyToParameter.Value = post.ReplyToPostId.HasValue ? post.ReplyToPostId.Value : DBNull.Value;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
