using Microsoft.AspNetCore.Identity;
using Npgsql;
using SpaceForum.Application.Discussions;
using SpaceForum.Application.Forums;
using SpaceForum.Application.Members;
using SpaceForum.Application.Security;
using SpaceForum.Domain.Discussions;
using SpaceForum.Domain.Forums;
using SpaceForum.Domain.Members;
using SpaceForum.Infrastructure.Identity;
using SpaceForum.Infrastructure.Persistence;

namespace SpaceForum.Web.Development;

internal sealed class DemoDataSeeder(
    UserManager<ApplicationUser> userManager,
    IMemberProfileRepository members,
    IForumCategoryRepository categories,
    IDiscussionRepository discussions,
    NpgsqlDataSource dataSource,
    IHostEnvironment environment,
    ILogger<DemoDataSeeder> logger)
{
    public const string AdminEmail = "admin@spaceforum.local";
    public const string MemberEmail = "member@spaceforum.local";
    public const string AdminPassword = "SpaceForum!2020";
    public const string MemberPassword = "ByteRanger!2020";

    private static readonly DateTimeOffset SeedDay = new(2020, 12, 3, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset CategoryCreatedAt = SeedDay.AddMinutes(15);
    private static readonly DateTimeOffset StressTopicCreatedAt = SeedDay.AddHours(8);
    private static readonly DateTimeOffset VoteCreatedAt = SeedDay.AddHours(17);

    private static readonly Action<ILogger, Exception?> LogProductionRefusal = LoggerMessage.Define(
        LogLevel.Error,
        new EventId(1, nameof(LogProductionRefusal)),
        "Demo data seeding is allowed only in Development.");

    private static readonly Action<ILogger, Exception?> LogSeedComplete = LoggerMessage.Define(
        LogLevel.Information,
        new EventId(2, nameof(LogSeedComplete)),
        "Development demo data is ready.");

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
        {
            LogProductionRefusal(logger, null);
            return 2;
        }

        var admin = await EnsureUserAsync(
            AdminEmail,
            "admin",
            "Space Admin",
            ForumRoles.Administrator,
            AdminPassword,
            cancellationToken);
        var member = await EnsureUserAsync(
            MemberEmail,
            "member",
            "Byte Ranger",
            ForumRoles.Member,
            MemberPassword,
            cancellationToken);

        var seededCategories = await EnsureCategoriesAsync(cancellationToken);
        await EnsureDiscussionsAsync(admin.Id, member.Id, seededCategories, cancellationToken);
        await EnsureCapabilityStressTopicAsync(
            admin.Id,
            member.Id,
            seededCategories["web-development"].Id,
            cancellationToken);
        await EnsureVoteAsync(admin.Id, "How should a growing .NET 10 forum solution be structured?", 1, VoteCreatedAt, cancellationToken);
        await EnsureVoteAsync(member.Id, "Welcome to the new SpaceForum", -1, VoteCreatedAt.AddMinutes(1), cancellationToken);
        await EnsureModernFeaturesAsync(admin.Id, member.Id, cancellationToken);
        await NormalizeDemoTimestampsAsync(admin.Id, member.Id, cancellationToken);
        await EnsureAuditEventsAsync(admin.Id, member.Id, cancellationToken);

        LogSeedComplete(logger, null);
        return 0;
    }

    private async Task<ApplicationUser> EnsureUserAsync(
        string email,
        string login,
        string displayName,
        string role,
        string initialPassword,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            user = new ApplicationUser
            {
                Id = Guid.CreateVersion7(SeedDay),
                UserName = email,
                Email = email,
                EmailConfirmed = true,
            };
            var createResult = await userManager.CreateAsync(user, initialPassword);
            EnsureSucceeded(createResult, $"create demo account {email}");
        }

        if (!await userManager.HasPasswordAsync(user))
        {
            EnsureSucceeded(
                await userManager.AddPasswordAsync(user, initialPassword),
                $"set initial development password for {email}");
        }

        if (!await userManager.IsInRoleAsync(user, ForumRoles.Member))
        {
            EnsureSucceeded(await userManager.AddToRoleAsync(user, ForumRoles.Member), $"assign Member to {email}");
        }

        if (!await userManager.IsInRoleAsync(user, role))
        {
            EnsureSucceeded(await userManager.AddToRoleAsync(user, role), $"assign {role} to {email}");
        }

        if (await members.FindByIdAsync(user.Id, trackChanges: false, cancellationToken) is null)
        {
            var profile = MemberProfile.Create(
                user.Id,
                login,
                displayName,
                SeedDay);
            if (!await members.TryAddAsync(profile, cancellationToken))
            {
                throw new InvalidOperationException($"Could not create demo profile {login}.");
            }
        }

        await using var updateProfile = dataSource.CreateCommand(
            """
            UPDATE members.member_profiles
            SET "Login" = @login,
                "DisplayName" = @displayName,
                "UpdatedAt" = @updatedAt,
                "Version" = "Version" + 1
            WHERE "Id" = @id
              AND ("Login" <> @login OR "DisplayName" <> @displayName);
            """);
        updateProfile.Parameters.AddWithValue("id", user.Id);
        updateProfile.Parameters.AddWithValue("login", login);
        updateProfile.Parameters.AddWithValue("displayName", displayName);
        updateProfile.Parameters.AddWithValue("updatedAt", SeedDay);
        await updateProfile.ExecuteNonQueryAsync(cancellationToken);

        return user;
    }

    private async Task EnsureVoteAsync(
        Guid memberId,
        string topicTitle,
        short value,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            INSERT INTO discussions.topic_votes ("TopicId", "MemberId", "Value", "CreatedAt", "UpdatedAt")
            SELECT topic."Id", @memberId, @value, @now, @now
            FROM discussions.topics AS topic
            WHERE topic."Title" = @topicTitle
            ON CONFLICT ("TopicId", "MemberId") DO NOTHING;
            """);
        command.Parameters.AddWithValue("memberId", memberId);
        command.Parameters.AddWithValue("value", value);
        command.Parameters.AddWithValue("now", createdAt);
        command.Parameters.AddWithValue("topicTitle", topicTitle);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<IReadOnlyDictionary<string, ForumCategory>> EnsureCategoriesAsync(
        CancellationToken cancellationToken)
    {
        var definitions = new[]
        {
            new CategorySeed("Announcements", "announcements", "News, releases, and important community updates.", CategoryFormat.Announcement, null, 0),
            new CategorySeed("Programming", "programming", "Languages, architecture, tools, and the craft of building software.", CategoryFormat.Discussion, null, 10),
            new CategorySeed("C# and .NET", "dotnet", "Questions and practical knowledge about modern C# and .NET.", CategoryFormat.QuestionAndAnswer, "programming", 0),
            new CategorySeed("Web development", "web-development", "Frontend, backend, accessibility, performance, and web standards.", CategoryFormat.QuestionAndAnswer, "programming", 10),
            new CategorySeed("Databases and DevOps", "databases-devops", "PostgreSQL, containers, observability, and reliable delivery.", CategoryFormat.Discussion, "programming", 20),
            new CategorySeed("Computer games", "computer-games", "Thoughtful conversations about games, communities, and the industry.", CategoryFormat.Discussion, null, 20),
            new CategorySeed("PC gaming", "pc-gaming", "What we play, co-op plans, performance, and recommendations.", CategoryFormat.Discussion, "computer-games", 0),
            new CategorySeed("Game development", "game-development", "Engines, gameplay systems, graphics, audio, and production.", CategoryFormat.QuestionAndAnswer, "computer-games", 10),
            new CategorySeed("Hardware and builds", "hardware-builds", "PC parts, peripherals, cooling, upgrades, and troubleshooting.", CategoryFormat.QuestionAndAnswer, "computer-games", 20),
        };

        var categories = new Dictionary<string, ForumCategory>(StringComparer.Ordinal);
        foreach (var definition in definitions.Where(definition => definition.ParentSlug is null))
        {
            categories[definition.Slug] = await EnsureCategoryAsync(definition, parentId: null, cancellationToken);
        }

        foreach (var definition in definitions.Where(definition => definition.ParentSlug is not null))
        {
            categories[definition.Slug] = await EnsureCategoryAsync(
                definition,
                categories[definition.ParentSlug!].Id,
                cancellationToken);
        }

        return categories;
    }

    private async Task<ForumCategory> EnsureCategoryAsync(
        CategorySeed definition,
        Guid? parentId,
        CancellationToken cancellationToken)
    {
        var category = await categories.FindBySlugAsync(definition.Slug, cancellationToken);
        if (category is not null)
        {
            return category;
        }

        category = ForumCategory.Create(
            Guid.CreateVersion7(CategoryCreatedAt),
            definition.Name,
            definition.Slug,
            definition.Description,
            definition.Format,
            parentId,
            definition.Position,
            CategoryCreatedAt);
        if (!await categories.TryAddAsync(category, cancellationToken))
        {
            return await categories.FindBySlugAsync(definition.Slug, cancellationToken)
                ?? throw new InvalidOperationException($"Could not create demo category {definition.Slug}.");
        }

        return category;
    }

    private async Task EnsureDiscussionsAsync(
        Guid adminId,
        Guid memberId,
        IReadOnlyDictionary<string, ForumCategory> categories,
        CancellationToken cancellationToken)
    {
        await EnsureTopicAsync(
            categories["announcements"],
            adminId,
            "Welcome to the new SpaceForum",
            "This is a development preview of a calmer community for durable discussions and useful answers. Explore the seeded categories, set passwords for the demo accounts through Mailpit, and try the member and administrator flows.",
            [(memberId, "The new structure already feels focused. I will use this topic to verify replies and public SSR reading.")],
            SeedDay.AddHours(1),
            cancellationToken);

        await EnsureTopicAsync(
            categories["dotnet"],
            memberId,
            "How should a growing .NET 10 forum solution be structured?",
            "I want clear module boundaries without starting with microservices. Which projects and dependency rules keep a Blazor forum maintainable as features grow?",
            [(adminId, "Start with a modular monolith: Domain and Application point inward, Infrastructure implements ports, and Web remains the composition root. Add vertical slices and split deployment only after measurements justify it.")],
            SeedDay.AddHours(2),
            cancellationToken);

        await EnsureTopicAsync(
            categories["pc-gaming"],
            memberId,
            "What are you playing on PC this week?",
            "Share one game you are enjoying and what makes it worth recommending. Spoiler-free descriptions are appreciated.",
            [(adminId, "I am revisiting a systems-heavy strategy game. It is a good reminder that understandable rules create deeper choices than a crowded interface.")],
            SeedDay.AddHours(3),
            cancellationToken);

        await EnsureTopicAsync(
            categories["game-development"],
            memberId,
            "Choosing an engine for a small multiplayer prototype",
            "The team is two programmers and one artist. We need quick iteration, basic networking, and desktop builds. What criteria should we test before committing to an engine?",
            [(adminId, "Build the same tiny vertical slice in your two strongest candidates. Measure iteration time, profiling quality, deployment friction, and how much networking behaviour you can test without a graphical client.")],
            SeedDay.AddHours(4),
            cancellationToken);
    }

    private async Task EnsureTopicAsync(
        ForumCategory category,
        Guid authorId,
        string title,
        string body,
        IReadOnlyList<(Guid AuthorId, string Body)> replies,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        await using var existsCommand = dataSource.CreateCommand(
            "SELECT EXISTS (SELECT 1 FROM discussions.topics WHERE \"Title\" = @title);"
        );
        existsCommand.Parameters.AddWithValue("title", title);
        if ((bool)(await existsCommand.ExecuteScalarAsync(cancellationToken) ?? false))
        {
            return;
        }

        var topic = Topic.Create(Guid.CreateVersion7(createdAt), category.Id, authorId, title, createdAt);
        var firstPost = Post.Create(Guid.CreateVersion7(createdAt), topic.Id, authorId, 1, body, createdAt);
        if (!await discussions.CreateAsync(topic, firstPost, cancellationToken))
        {
            throw new InvalidOperationException($"Could not create demo topic '{title}'.");
        }

        var postNumber = 2;
        foreach (var reply in replies)
        {
            var repliedAt = createdAt.AddMinutes(postNumber - 1);
            var trackedTopic = await discussions.FindTopicAsync(topic.Id, trackChanges: true, cancellationToken)
                ?? throw new InvalidOperationException($"Demo topic '{title}' was not found after creation.");
            trackedTopic.RecordReply(repliedAt);
            var post = Post.Create(
                Guid.CreateVersion7(repliedAt),
                topic.Id,
                reply.AuthorId,
                postNumber,
                reply.Body,
                repliedAt);
            if (!await discussions.AddReplyAsync(post, cancellationToken))
            {
                throw new InvalidOperationException($"Could not add demo reply to '{title}'.");
            }

            postNumber++;
        }
    }

    private async Task EnsureCapabilityStressTopicAsync(
        Guid adminId,
        Guid memberId,
        Guid categoryId,
        CancellationToken cancellationToken)
    {
        const string title = "Forum capability stress test";
        const int postCount = 500;
        var createdAt = StressTopicCreatedAt;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var ensureTopic = connection.CreateCommand();
        ensureTopic.Transaction = transaction;
        ensureTopic.CommandText =
            """
            INSERT INTO discussions.topics
                ("Id", "CategoryId", "AuthorId", "Slug", "Title", "CreatedAt", "LastActivityAt", "ReplyCount", "IsClosed", "Version")
            SELECT @topicId, @categoryId, @adminId, @slug, @title, @createdAt,
                   @lastActivityAt, @replyCount, false, @version
            WHERE NOT EXISTS (SELECT 1 FROM discussions.topics WHERE "Title" = @title)
            RETURNING "Id";
            """;
        var proposedTopicId = Guid.CreateVersion7(createdAt);
        ensureTopic.Parameters.AddWithValue("topicId", proposedTopicId);
        ensureTopic.Parameters.AddWithValue("categoryId", categoryId);
        ensureTopic.Parameters.AddWithValue("adminId", adminId);
        ensureTopic.Parameters.AddWithValue("slug", TopicSlug.Create(title));
        ensureTopic.Parameters.AddWithValue("title", title);
        ensureTopic.Parameters.AddWithValue("createdAt", createdAt);
        ensureTopic.Parameters.AddWithValue("lastActivityAt", createdAt.AddMinutes(postCount - 1));
        ensureTopic.Parameters.AddWithValue("replyCount", postCount - 1);
        ensureTopic.Parameters.AddWithValue("version", (long)postCount);
        var insertedId = await ensureTopic.ExecuteScalarAsync(cancellationToken);

        Guid topicId;
        if (insertedId is Guid createdTopicId)
        {
            topicId = createdTopicId;
        }
        else
        {
            await using var findTopic = connection.CreateCommand();
            findTopic.Transaction = transaction;
            findTopic.CommandText = "SELECT \"Id\" FROM discussions.topics WHERE \"Title\" = @title;";
            findTopic.Parameters.AddWithValue("title", title);
            topicId = (Guid)(await findTopic.ExecuteScalarAsync(cancellationToken)
                ?? throw new InvalidOperationException("The capability stress-test topic could not be found."));
        }

        await using var insertPosts = connection.CreateCommand();
        insertPosts.Transaction = transaction;
        insertPosts.CommandText =
            """
            INSERT INTO discussions.posts
                ("Id", "TopicId", "AuthorId", "Number", "Body", "CreatedAt", "UpdatedAt", "Version")
            SELECT
                uuidv7((@createdAt + ((series.number - 1) * interval '1 minute')) - statement_timestamp()),
                @topicId,
                CASE WHEN series.number % 2 = 1 THEN @adminId ELSE @memberId END,
                series.number,
                CASE
                    WHEN series.number = 1 THEN 'This seeded discussion contains 500 replies from two members. It exercises long post streams, the post navigator, anchors, local time rendering, search, permissions, and moderation controls.'
                    ELSE format('Capability check message %s of %s. Author alternates between Space Admin and Byte Ranger so profile links, navigation, and moderation can be reviewed on a realistic long discussion.', series.number, @postCount)
                END,
                @createdAt + ((series.number - 1) * interval '1 minute'),
                @createdAt + ((series.number - 1) * interval '1 minute'),
                1
            FROM generate_series(1, @postCount) AS series(number)
            WHERE NOT EXISTS (
                SELECT 1
                FROM discussions.posts AS existing
                WHERE existing."TopicId" = @topicId AND existing."Number" = series.number);
            """;
        insertPosts.Parameters.AddWithValue("topicId", topicId);
        insertPosts.Parameters.AddWithValue("adminId", adminId);
        insertPosts.Parameters.AddWithValue("memberId", memberId);
        insertPosts.Parameters.AddWithValue("postCount", postCount);
        insertPosts.Parameters.AddWithValue("createdAt", createdAt);
        await insertPosts.ExecuteNonQueryAsync(cancellationToken);

        await using var synchronizeTopic = connection.CreateCommand();
        synchronizeTopic.Transaction = transaction;
        synchronizeTopic.CommandText =
            """
            UPDATE discussions.topics
            SET "ReplyCount" = (SELECT COUNT(*)::int - 1 FROM discussions.posts WHERE "TopicId" = @topicId),
                "LastActivityAt" = (SELECT MAX("CreatedAt") FROM discussions.posts WHERE "TopicId" = @topicId),
                "Version" = GREATEST("Version", @postCount)
            WHERE "Id" = @topicId;
            """;
        synchronizeTopic.Parameters.AddWithValue("topicId", topicId);
        synchronizeTopic.Parameters.AddWithValue("postCount", (long)postCount);
        await synchronizeTopic.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    private async Task NormalizeDemoTimestampsAsync(
        Guid adminId,
        Guid memberId,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            UPDATE members.member_profiles
            SET "CreatedAt" = @profileCreatedAt,
                "UpdatedAt" = @profileCreatedAt
            WHERE "Id" IN (@adminId, @memberId);

            UPDATE forums.categories
            SET "CreatedAt" = @categoryCreatedAt,
                "UpdatedAt" = @categoryCreatedAt
            WHERE "Slug" IN (
                'announcements', 'programming', 'dotnet', 'web-development',
                'databases-devops', 'computer-games', 'pc-gaming',
                'game-development', 'hardware-builds');

            WITH seed_topics("Title", "CreatedAt") AS (
                VALUES
                    ('Welcome to the new SpaceForum', @welcomeCreatedAt),
                    ('How should a growing .NET 10 forum solution be structured?', @dotnetCreatedAt),
                    ('What are you playing on PC this week?', @gamingCreatedAt),
                    ('Choosing an engine for a small multiplayer prototype', @engineCreatedAt),
                    ('Forum capability stress test', @stressCreatedAt)
            )
            UPDATE discussions.posts AS post
            SET "CreatedAt" = seed."CreatedAt" + ((post."Number" - 1) * interval '1 minute'),
                "UpdatedAt" = seed."CreatedAt" + ((post."Number" - 1) * interval '1 minute')
            FROM discussions.topics AS topic
            INNER JOIN seed_topics AS seed ON seed."Title" = topic."Title"
            WHERE post."TopicId" = topic."Id";

            WITH seed_topics("Title", "CreatedAt") AS (
                VALUES
                    ('Welcome to the new SpaceForum', @welcomeCreatedAt),
                    ('How should a growing .NET 10 forum solution be structured?', @dotnetCreatedAt),
                    ('What are you playing on PC this week?', @gamingCreatedAt),
                    ('Choosing an engine for a small multiplayer prototype', @engineCreatedAt),
                    ('Forum capability stress test', @stressCreatedAt)
            )
            UPDATE discussions.topics AS topic
            SET "CreatedAt" = seed."CreatedAt",
                "LastActivityAt" = (
                    SELECT MAX(post."CreatedAt")
                    FROM discussions.posts AS post
                    WHERE post."TopicId" = topic."Id")
            FROM seed_topics AS seed
            WHERE seed."Title" = topic."Title";

            UPDATE discussions.topic_votes AS vote
            SET "CreatedAt" = @voteCreatedAt,
                "UpdatedAt" = @voteCreatedAt
            FROM discussions.topics AS topic
            WHERE vote."TopicId" = topic."Id"
              AND vote."MemberId" IN (@adminId, @memberId)
              AND topic."Title" IN (
                  'Welcome to the new SpaceForum',
                  'How should a growing .NET 10 forum solution be structured?');
            """);
        command.Parameters.AddWithValue("adminId", adminId);
        command.Parameters.AddWithValue("memberId", memberId);
        command.Parameters.AddWithValue("profileCreatedAt", SeedDay);
        command.Parameters.AddWithValue("categoryCreatedAt", CategoryCreatedAt);
        command.Parameters.AddWithValue("welcomeCreatedAt", SeedDay.AddHours(1));
        command.Parameters.AddWithValue("dotnetCreatedAt", SeedDay.AddHours(2));
        command.Parameters.AddWithValue("gamingCreatedAt", SeedDay.AddHours(3));
        command.Parameters.AddWithValue("engineCreatedAt", SeedDay.AddHours(4));
        command.Parameters.AddWithValue("stressCreatedAt", StressTopicCreatedAt);
        command.Parameters.AddWithValue("voteCreatedAt", VoteCreatedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task EnsureModernFeaturesAsync(Guid adminId, Guid memberId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            INSERT INTO discussions.tags ("Id", "Name", "Slug", "Description", "Color", "Position") VALUES
                ('0193a56d-64c0-7000-8000-000000000101', '.NET', 'dotnet', '.NET platform and C# discussions.', '#6d28d9', 0),
                ('0193a56d-64c0-7000-8000-000000000102', 'PostgreSQL', 'postgresql', 'PostgreSQL databases and operations.', '#2563eb', 10),
                ('0193a56d-64c0-7000-8000-000000000103', 'Gaming', 'gaming', 'Computer games and game development.', '#a21caf', 20)
            ON CONFLICT ("Slug") DO UPDATE SET "Name" = EXCLUDED."Name", "Description" = EXCLUDED."Description", "Color" = EXCLUDED."Color", "Position" = EXCLUDED."Position";

            INSERT INTO discussions.topic_tags ("TopicId", "TagId")
            SELECT topic."Id", tag."Id" FROM discussions.topics AS topic CROSS JOIN discussions.tags AS tag
            WHERE (topic."Title" LIKE '%NET 10%' AND tag."Slug" = 'dotnet')
               OR (topic."Title" LIKE '%playing%' AND tag."Slug" = 'gaming')
            ON CONFLICT DO NOTHING;

            INSERT INTO discussions.topic_subscriptions ("TopicId", "MemberId", "State", "LastReadPostNumber", "UpdatedAt")
            SELECT topic."Id", @memberId, 'Following', GREATEST(1, topic."ReplyCount"), @seedAt
            FROM discussions.topics AS topic WHERE topic."Title" = 'Welcome to the new SpaceForum'
            ON CONFLICT ("TopicId", "MemberId") DO NOTHING;

            INSERT INTO configuration.forum_settings ("Key", "Value", "UpdatedAt", "UpdatedByMemberId") VALUES
                ('forum.name', '"SpaceForum"'::jsonb, @seedAt, @adminId),
                ('forum.description', '"Programming, gaming, and durable community knowledge."'::jsonb, @seedAt, @adminId),
                ('forum.welcome', '"Good conversations deserve room to breathe."'::jsonb, @seedAt, @adminId),
                ('appearance.accent', '"#6d28d9"'::jsonb, @seedAt, @adminId),
                ('advanced.maintenance', '"false"'::jsonb, @seedAt, @adminId)
            ON CONFLICT ("Key") DO UPDATE SET "Value" = EXCLUDED."Value", "UpdatedAt" = EXCLUDED."UpdatedAt", "UpdatedByMemberId" = EXCLUDED."UpdatedByMemberId";
            """);
        command.Parameters.AddWithValue("adminId", adminId);
        command.Parameters.AddWithValue("memberId", memberId);
        command.Parameters.AddWithValue("seedAt", SeedDay.AddHours(18));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task EnsureAuditEventsAsync(Guid adminId, Guid memberId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            INSERT INTO audit.security_audit_events
                ("Id", "OccurredAt", "EventType", "ActorMemberId", "SubjectId", "Succeeded")
            VALUES
                ('0193a56d-64c0-7000-8000-000000000201', @adminRegisteredAt, 'identity.account.registered', @adminId, @adminSubject, true),
                ('0193a56d-64c0-7000-8000-000000000202', @adminPromotedAt, 'identity.administrator.bootstrapped', @adminId, @adminSubject, true),
                ('0193a56d-64c0-7000-8000-000000000203', @memberRegisteredAt, 'identity.account.registered', @memberId, @memberSubject, true),
                ('0193a56d-64c0-7000-8000-000000000204', @memberLoginAt, 'identity.login.succeeded', @memberId, @memberSubject, true)
            ON CONFLICT ("Id") DO UPDATE SET
                "OccurredAt" = EXCLUDED."OccurredAt",
                "EventType" = EXCLUDED."EventType",
                "ActorMemberId" = EXCLUDED."ActorMemberId",
                "SubjectId" = EXCLUDED."SubjectId",
                "Succeeded" = EXCLUDED."Succeeded";
            """);
        command.Parameters.AddWithValue("adminRegisteredAt", SeedDay.AddMinutes(5));
        command.Parameters.AddWithValue("adminPromotedAt", SeedDay.AddMinutes(6));
        command.Parameters.AddWithValue("memberRegisteredAt", SeedDay.AddMinutes(7));
        command.Parameters.AddWithValue("memberLoginAt", SeedDay.AddMinutes(8));
        command.Parameters.AddWithValue("adminId", adminId);
        command.Parameters.AddWithValue("memberId", memberId);
        command.Parameters.AddWithValue("adminSubject", adminId.ToString());
        command.Parameters.AddWithValue("memberSubject", memberId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void EnsureSucceeded(IdentityResult result, string operation)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Could not {operation}: {string.Join(", ", result.Errors.Select(error => error.Code))}");
        }
    }

    private sealed record CategorySeed(
        string Name,
        string Slug,
        string Description,
        CategoryFormat Format,
        string? ParentSlug,
        int Position);
}
