using SpaceForum.Application.Forums;
using SpaceForum.Application.Members;
using SpaceForum.Application.Security;
using SpaceForum.Domain.Forums;
using SpaceForum.Domain.Members;

namespace SpaceForum.Application.Tests.Forums;

public sealed class DeleteCategoryHandlerTests
{
    private static readonly DateTimeOffset Now = new(2020, 12, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RefusesNonAdministratorBeforeReadingMemberOrCategory()
    {
        var categories = new FakeCategoryRepository(deleteSucceeds: true);
        var members = new FakeMemberRepository(CreateMember("admin"));
        var handler = new DeleteCategoryHandler(categories, members, new FakeAccess(false), new FakeAuditWriter());

        var result = await handler.HandleAsync(members.Member.Id, categories.Category.Id, CancellationToken.None);

        Assert.Equal(DeleteCategoryStatus.Forbidden, result.Status);
        Assert.False(members.WasRead);
        Assert.False(categories.DeleteAttempted);
    }

    [Fact]
    public async Task RefusesAdministratorWhoseLoginIsNotAdmin()
    {
        var categories = new FakeCategoryRepository(deleteSucceeds: true);
        var members = new FakeMemberRepository(CreateMember("moderator"));
        var handler = new DeleteCategoryHandler(categories, members, new FakeAccess(true), new FakeAuditWriter());

        var result = await handler.HandleAsync(members.Member.Id, categories.Category.Id, CancellationToken.None);

        Assert.Equal(DeleteCategoryStatus.Forbidden, result.Status);
        Assert.False(categories.DeleteAttempted);
    }

    [Fact]
    public async Task RefusesCategoryThatStillHasDependencies()
    {
        var categories = new FakeCategoryRepository(deleteSucceeds: false);
        var members = new FakeMemberRepository(CreateMember("admin"));
        var handler = new DeleteCategoryHandler(categories, members, new FakeAccess(true), new FakeAuditWriter());

        var result = await handler.HandleAsync(members.Member.Id, categories.Category.Id, CancellationToken.None);

        Assert.Equal(DeleteCategoryStatus.NotEmpty, result.Status);
        Assert.True(categories.DeleteAttempted);
    }

    [Fact]
    public async Task DeletesEmptyCategoryAndWritesAuditForRootAdministrator()
    {
        var categories = new FakeCategoryRepository(deleteSucceeds: true);
        var members = new FakeMemberRepository(CreateMember("admin"));
        var audit = new FakeAuditWriter();
        var handler = new DeleteCategoryHandler(categories, members, new FakeAccess(true), audit);

        var result = await handler.HandleAsync(members.Member.Id, categories.Category.Id, CancellationToken.None);

        Assert.Equal(DeleteCategoryStatus.Deleted, result.Status);
        Assert.Equal(SecurityEventTypes.CategoryDeleted, audit.Record?.EventType);
        Assert.Equal(categories.Category.Id.ToString(), audit.Record?.SubjectId);
    }

    private static MemberProfile CreateMember(string login) =>
        MemberProfile.Create(Guid.CreateVersion7(Now), login, "Test administrator", Now);

    private sealed class FakeAccess(bool allowed) : IForumAdministrationAccess
    {
        public Task<bool> CanAdministerAsync(Guid memberId, CancellationToken cancellationToken) =>
            Task.FromResult(allowed);
    }

    private sealed class FakeAuditWriter : ISecurityAuditWriter
    {
        public SecurityAuditRecord? Record { get; private set; }

        public Task WriteAsync(SecurityAuditRecord record, CancellationToken cancellationToken)
        {
            Record = record;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeMemberRepository(MemberProfile member) : IMemberProfileRepository
    {
        public MemberProfile Member { get; } = member;

        public bool WasRead { get; private set; }

        public Task<bool> TryAddAsync(MemberProfile memberProfile, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<bool> LoginExistsAsync(string normalizedLogin, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<MemberProfile?> FindByIdAsync(Guid id, bool trackChanges, CancellationToken cancellationToken)
        {
            WasRead = true;
            return Task.FromResult<MemberProfile?>(id == Member.Id ? Member : null);
        }

        public Task<MemberProfile?> FindByLoginAsync(string normalizedLogin, CancellationToken cancellationToken) =>
            Task.FromResult<MemberProfile?>(Member.Login == normalizedLogin ? Member : null);

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeCategoryRepository(bool deleteSucceeds) : IForumCategoryRepository
    {
        public ForumCategory Category { get; } = ForumCategory.Create(
            Guid.CreateVersion7(Now),
            "Empty category",
            "empty-category",
            "Safe to delete in a test.",
            CategoryFormat.Discussion,
            null,
            0,
            Now);

        public bool DeleteAttempted { get; private set; }

        public Task<IReadOnlyList<ForumCategory>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ForumCategory>>([Category]);

        public Task<ForumCategory?> FindBySlugAsync(string normalizedSlug, CancellationToken cancellationToken) =>
            Task.FromResult<ForumCategory?>(Category.Slug == normalizedSlug ? Category : null);

        public Task<ForumCategory?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<ForumCategory?>(Category.Id == id ? Category : null);

        public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Category.Id == id);

        public Task<bool> SlugExistsAsync(string normalizedSlug, CancellationToken cancellationToken) =>
            Task.FromResult(Category.Slug == normalizedSlug);

        public Task<bool> TryAddAsync(ForumCategory category, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<bool> TryDeleteEmptyAsync(Guid id, CancellationToken cancellationToken)
        {
            DeleteAttempted = true;
            return Task.FromResult(id == Category.Id && deleteSucceeds);
        }
    }
}
