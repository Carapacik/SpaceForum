using SpaceForum.Application.Forums;
using SpaceForum.Domain.Forums;

namespace SpaceForum.Application.Tests.Forums;

public sealed class CreateCategoryHandlerTests
{
    private static readonly DateTimeOffset Now = new(2020, 12, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RefusesNonAdministratorBeforeReadingCategoryData()
    {
        var repository = new FakeCategoryRepository();
        var handler = new CreateCategoryHandler(repository, new FakeAccess(false), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(
            new(Guid.CreateVersion7(), "Secret", "secret", "Not allowed", CategoryFormat.Discussion, null, 0),
            CancellationToken.None);

        Assert.Equal(CreateCategoryStatus.Forbidden, result.Status);
        Assert.False(repository.WasQueried);
        Assert.Null(repository.Added);
    }

    [Fact]
    public async Task RejectsMissingParent()
    {
        var repository = new FakeCategoryRepository();
        var handler = new CreateCategoryHandler(repository, new FakeAccess(true), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(
            new(Guid.CreateVersion7(), "Child", "child", "Child category", CategoryFormat.Discussion, Guid.CreateVersion7(), 0),
            CancellationToken.None);

        Assert.Equal(CreateCategoryStatus.ParentNotFound, result.Status);
        Assert.Null(repository.Added);
    }

    [Fact]
    public async Task CreatesValidatedCategoryForAdministrator()
    {
        var repository = new FakeCategoryRepository();
        var handler = new CreateCategoryHandler(repository, new FakeAccess(true), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(
            new(Guid.CreateVersion7(), "C# and .NET", "DOTNET", "Modern .NET questions", CategoryFormat.QuestionAndAnswer, null, 10),
            CancellationToken.None);

        Assert.Equal(CreateCategoryStatus.Created, result.Status);
        Assert.Equal("dotnet", repository.Added?.Slug);
        Assert.Equal(CategoryFormat.QuestionAndAnswer, repository.Added?.Format);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeAccess(bool allowed) : IForumAdministrationAccess
    {
        public Task<bool> CanAdministerAsync(Guid memberId, CancellationToken cancellationToken) => Task.FromResult(allowed);
    }

    private sealed class FakeCategoryRepository : IForumCategoryRepository
    {
        public bool WasQueried { get; private set; }

        public ForumCategory? Added { get; private set; }

        public Task<IReadOnlyList<ForumCategory>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ForumCategory>>([]);

        public Task<ForumCategory?> FindBySlugAsync(string normalizedSlug, CancellationToken cancellationToken) =>
            Task.FromResult<ForumCategory?>(null);

        public Task<ForumCategory?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<ForumCategory?>(null);

        public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken)
        {
            WasQueried = true;
            return Task.FromResult(false);
        }

        public Task<bool> SlugExistsAsync(string normalizedSlug, CancellationToken cancellationToken)
        {
            WasQueried = true;
            return Task.FromResult(false);
        }

        public Task<bool> TryAddAsync(ForumCategory category, CancellationToken cancellationToken)
        {
            Added = category;
            return Task.FromResult(true);
        }

        public Task<bool> TryDeleteEmptyAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(false);
    }
}
