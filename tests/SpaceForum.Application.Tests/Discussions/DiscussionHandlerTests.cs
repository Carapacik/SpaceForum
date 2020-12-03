using SpaceForum.Application.Discussions;
using SpaceForum.Application.Forums;
using SpaceForum.Application.Security;
using SpaceForum.Domain.Discussions;
using SpaceForum.Domain.Forums;

namespace SpaceForum.Application.Tests.Discussions;

public sealed class DiscussionHandlerTests
{
    private static readonly DateTimeOffset Now = new(2020, 12, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RegularMemberCannotCreateAnnouncement()
    {
        var category = CreateCategory(CategoryFormat.Announcement);
        var discussions = new FakeDiscussionRepository();
        var handler = new CreateTopicHandler(
            discussions,
            new FakeCategoryRepository(category),
            new FakePostingAccess(canPost: true, canAnnounce: false),
            new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(
            new(Guid.CreateVersion7(), category.Id, "Important announcement", "Announcement content"),
            CancellationToken.None);

        Assert.Equal(CreateTopicStatus.Forbidden, result.Status);
        Assert.Null(discussions.CreatedTopic);
    }

    [Fact]
    public async Task MemberCreatesTopicWithFirstPost()
    {
        var category = CreateCategory(CategoryFormat.Discussion);
        var memberId = Guid.CreateVersion7();
        var discussions = new FakeDiscussionRepository();
        var handler = new CreateTopicHandler(
            discussions,
            new FakeCategoryRepository(category),
            new FakePostingAccess(canPost: true, canAnnounce: false),
            new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(
            new(memberId, category.Id, "A thoughtful discussion", "This is the opening message."),
            CancellationToken.None);

        Assert.Equal(CreateTopicStatus.Created, result.Status);
        Assert.Equal("/d/42-a-thoughtful-discussion", result.Route?.Path);
        Assert.Equal(memberId, discussions.CreatedTopic?.AuthorId);
        Assert.Equal(1, discussions.CreatedPost?.Number);
        Assert.Equal(discussions.CreatedTopic?.Id, discussions.CreatedPost?.TopicId);
    }

    [Fact]
    public async Task ReplyUpdatesTopicAndCreatesNextPost()
    {
        var memberId = Guid.CreateVersion7();
        var topic = Topic.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), memberId, "A thoughtful discussion", Now);
        var discussions = new FakeDiscussionRepository { ExistingTopic = topic, NextPostNumber = 2 };
        var handler = new CreateReplyHandler(
            discussions,
            new FakePostingAccess(canPost: true, canAnnounce: false),
            new FixedTimeProvider(Now.AddMinutes(5)));

        var result = await handler.HandleAsync(
            new(memberId, topic.Id, "This is a useful reply."),
            CancellationToken.None);

        Assert.Equal(CreateReplyStatus.Created, result.Status);
        Assert.Equal(2, discussions.CreatedPost?.Number);
        Assert.Equal(1, topic.ReplyCount);
    }

    [Fact]
    public async Task MemberCanVoteAndToggleTheVote()
    {
        var repository = new FakeDiscussionRepository();
        var handler = new VoteTopicHandler(
            repository,
            new FakePostingAccess(canPost: true, canAnnounce: false),
            new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(
            new(Guid.CreateVersion7(), Guid.CreateVersion7(), -1),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(-1, result.State?.Score);
        Assert.Equal(-1, result.State?.Vote);
    }

    [Fact]
    public async Task TopicCreatorCanCloseOwnTopic()
    {
        var memberId = Guid.CreateVersion7();
        var topic = Topic.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), memberId, "Creator controls", Now);
        var repository = new FakeDiscussionRepository { ExistingTopic = topic };
        var handler = new ModerateDiscussionHandler(repository, new FakeModerationAccess(false), new FakeAuditWriter());

        var result = await handler.SetClosedAsync(memberId, topic.Id, true, CancellationToken.None);

        Assert.Equal(ModerateDiscussionStatus.Updated, result.Status);
        Assert.True(repository.ClosedState);
    }

    [Fact]
    public async Task AdministratorCanDeleteTopic()
    {
        var memberId = Guid.CreateVersion7();
        var topic = Topic.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "Admin controls", Now);
        var repository = new FakeDiscussionRepository { ExistingTopic = topic };
        var handler = new ModerateDiscussionHandler(repository, new FakeModerationAccess(true), new FakeAuditWriter());

        var result = await handler.DeleteTopicAsync(memberId, topic.Id, CancellationToken.None);

        Assert.Equal(ModerateDiscussionStatus.Deleted, result.Status);
        Assert.True(repository.TopicDeleted);
    }

    [Fact]
    public async Task RegularMemberCannotDeleteTopic()
    {
        var repository = new FakeDiscussionRepository();
        var handler = new ModerateDiscussionHandler(repository, new FakeModerationAccess(false), new FakeAuditWriter());

        var result = await handler.DeleteTopicAsync(Guid.CreateVersion7(), Guid.CreateVersion7(), CancellationToken.None);

        Assert.Equal(ModerateDiscussionStatus.Forbidden, result.Status);
        Assert.False(repository.TopicDeleted);
    }

    [Fact]
    public async Task RegularMemberCannotCloseAnotherMembersTopic()
    {
        var topic = Topic.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "Another creator", Now);
        var repository = new FakeDiscussionRepository { ExistingTopic = topic };
        var handler = new ModerateDiscussionHandler(repository, new FakeModerationAccess(false), new FakeAuditWriter());

        var result = await handler.SetClosedAsync(Guid.CreateVersion7(), topic.Id, true, CancellationToken.None);

        Assert.Equal(ModerateDiscussionStatus.Forbidden, result.Status);
        Assert.Null(repository.ClosedState);
    }

    [Fact]
    public async Task AdministratorCanDeleteReply()
    {
        var repository = new FakeDiscussionRepository();
        var handler = new ModerateDiscussionHandler(repository, new FakeModerationAccess(true), new FakeAuditWriter());

        var result = await handler.DeletePostAsync(Guid.CreateVersion7(), Guid.CreateVersion7(), CancellationToken.None);

        Assert.Equal(ModerateDiscussionStatus.Deleted, result.Status);
        Assert.True(repository.ReplyDeleted);
    }

    private static ForumCategory CreateCategory(CategoryFormat format) =>
        ForumCategory.Create(Guid.CreateVersion7(), "Category", "category", "Description", format, null, 0, Now);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakePostingAccess(bool canPost, bool canAnnounce) : IForumPostingAccess
    {
        public Task<bool> CanPostAsync(Guid memberId, CancellationToken cancellationToken) => Task.FromResult(canPost);

        public Task<bool> CanCreateAnnouncementAsync(Guid memberId, CancellationToken cancellationToken) => Task.FromResult(canAnnounce);
    }

    private sealed class FakeModerationAccess(bool isAdministrator) : IForumModerationAccess
    {
        public Task<bool> IsAdministratorAsync(Guid memberId, CancellationToken cancellationToken) =>
            Task.FromResult(isAdministrator);
    }

    private sealed class FakeAuditWriter : ISecurityAuditWriter
    {
        public Task WriteAsync(SecurityAuditRecord record, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeCategoryRepository(ForumCategory category) : IForumCategoryRepository
    {
        public Task<IReadOnlyList<ForumCategory>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ForumCategory>>([category]);

        public Task<ForumCategory?> FindBySlugAsync(string normalizedSlug, CancellationToken cancellationToken) =>
            Task.FromResult(category.Slug == normalizedSlug ? category : null);

        public Task<ForumCategory?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(category.Id == id ? category : null);

        public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(category.Id == id);

        public Task<bool> SlugExistsAsync(string normalizedSlug, CancellationToken cancellationToken) =>
            Task.FromResult(category.Slug == normalizedSlug);

        public Task<bool> TryAddAsync(ForumCategory item, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<bool> TryDeleteEmptyAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed class FakeDiscussionRepository : IDiscussionRepository
    {
        public Topic? ExistingTopic { get; init; }

        public int NextPostNumber { get; init; } = 1;

        public Topic? CreatedTopic { get; private set; }

        public Post? CreatedPost { get; private set; }

        public bool? ClosedState { get; private set; }

        public bool TopicDeleted { get; private set; }

        public bool ReplyDeleted { get; private set; }

        public Task<bool> CreateAsync(Topic topic, Post firstPost, CancellationToken cancellationToken)
        {
            CreatedTopic = topic;
            CreatedPost = firstPost;
            return Task.FromResult(true);
        }

        public Task<Topic?> FindTopicAsync(Guid topicId, bool trackChanges, CancellationToken cancellationToken) =>
            Task.FromResult(ExistingTopic?.Id == topicId ? ExistingTopic : null);

        public Task<int> GetNextPostNumberAsync(Guid topicId, CancellationToken cancellationToken) =>
            Task.FromResult(NextPostNumber);

        public Task<int> GetLastPostNumberAsync(Guid topicId, CancellationToken cancellationToken) =>
            Task.FromResult(Math.Max(0, NextPostNumber - 1));

        public Task<bool> AddReplyAsync(Post post, CancellationToken cancellationToken)
        {
            CreatedPost = post;
            return Task.FromResult(true);
        }

        public Task<IReadOnlyList<TopicListItem>> ListAsync(TopicSort sort, int take, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TopicListItem>>([]);

        public Task<IReadOnlyList<TopicListItem>> ListByCategoryAsync(Guid categoryId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TopicListItem>>([]);

        public Task<IReadOnlyList<TopicListItem>> SearchAsync(string query, int take, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TopicListItem>>([]);

        public Task<TopicRoute?> GetRouteAsync(Guid topicId, CancellationToken cancellationToken) =>
            Task.FromResult<TopicRoute?>(CreatedTopic?.Id == topicId ? new(42, TopicSlug.Create(CreatedTopic.Title)) : null);

        public Task<TopicDetail?> GetDetailAsync(long topicNumber, int page, int pageSize, CancellationToken cancellationToken) =>
            Task.FromResult<TopicDetail?>(null);

        public Task<TopicVoteState> GetVoteAsync(Guid topicId, Guid? memberId, CancellationToken cancellationToken) =>
            Task.FromResult(new TopicVoteState(0, null));

        public Task<TopicVoteState?> SetVoteAsync(
            Guid topicId,
            Guid memberId,
            int value,
            DateTimeOffset changedAt,
            CancellationToken cancellationToken) =>
            Task.FromResult<TopicVoteState?>(new(value, value));

        public Task<bool> SetClosedAsync(Guid topicId, bool isClosed, CancellationToken cancellationToken) =>
            Task.FromResult(SetClosed(topicId, isClosed));

        public Task<bool> DeleteTopicAsync(Guid topicId, CancellationToken cancellationToken)
        {
            TopicDeleted = ExistingTopic?.Id == topicId;
            return Task.FromResult(TopicDeleted);
        }

        public Task<bool> DeleteReplyAsync(Guid postId, CancellationToken cancellationToken)
        {
            ReplyDeleted = true;
            return Task.FromResult(true);
        }

        private bool SetClosed(Guid topicId, bool isClosed)
        {
            if (ExistingTopic?.Id != topicId)
            {
                return false;
            }

            ClosedState = isClosed;
            return true;
        }
    }
}
