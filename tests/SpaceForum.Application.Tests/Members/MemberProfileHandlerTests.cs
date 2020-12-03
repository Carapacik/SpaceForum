using SpaceForum.Application.Members;
using SpaceForum.Domain.Members;

namespace SpaceForum.Application.Tests.Members;

public sealed class MemberProfileHandlerTests
{
    private static readonly DateTimeOffset Now = new(2020, 12, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreateReturnsLoginUnavailableWithoutAddingProfile()
    {
        var repository = new FakeMemberProfileRepository { ExistingLogin = "taken" };
        var handler = new CreateMemberProfileHandler(repository, new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(
            new(Guid.CreateVersion7(), "Taken", "Member"),
            CancellationToken.None);

        Assert.Equal(CreateMemberProfileStatus.LoginUnavailable, result.Status);
        Assert.Null(repository.AddedMember);
        Assert.Equal(0, repository.SaveCount);
    }

    [Fact]
    public async Task CreateAddsAValidatedProfile()
    {
        var repository = new FakeMemberProfileRepository();
        var id = Guid.CreateVersion7();
        var handler = new CreateMemberProfileHandler(repository, new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(
            new(id, "SpaceUser", "Alex Morgan"),
            CancellationToken.None);

        Assert.Equal(CreateMemberProfileStatus.Created, result.Status);
        Assert.Equal(id, repository.AddedMember?.Id);
        Assert.Equal("spaceuser", repository.AddedMember?.Login);
        Assert.Equal(1, repository.SaveCount);
    }

    [Fact]
    public async Task CreateHandlesAConcurrentLoginConflict()
    {
        var repository = new FakeMemberProfileRepository { TryAddSucceeds = false };
        var handler = new CreateMemberProfileHandler(repository, new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(
            new(Guid.CreateVersion7(), "available", "Member"),
            CancellationToken.None);

        Assert.Equal(CreateMemberProfileStatus.LoginUnavailable, result.Status);
        Assert.Equal(1, repository.SaveCount);
    }

    [Fact]
    public async Task UpdateRefusesHorizontalPrivilegeEscalation()
    {
        var profileId = Guid.CreateVersion7();
        var repository = new FakeMemberProfileRepository
        {
            Member = MemberProfile.Create(profileId, "member", "Member", Now),
        };
        var handler = new UpdateMemberProfileHandler(repository, new FixedTimeProvider(Now.AddMinutes(1)));

        var result = await handler.HandleAsync(
            new(Guid.CreateVersion7(), profileId, "Attacker", null, null, null),
            CancellationToken.None);

        Assert.Equal(UpdateMemberProfileStatus.Forbidden, result.Status);
        Assert.False(repository.FindByIdCalled);
        Assert.Equal("Member", repository.Member.DisplayName);
        Assert.Equal(0, repository.SaveCount);
    }

    [Fact]
    public async Task UpdateChangesTheActorsOwnProfile()
    {
        var profileId = Guid.CreateVersion7();
        var repository = new FakeMemberProfileRepository
        {
            Member = MemberProfile.Create(profileId, "member", "Member", Now),
        };
        var handler = new UpdateMemberProfileHandler(repository, new FixedTimeProvider(Now.AddMinutes(1)));

        var result = await handler.HandleAsync(
            new(profileId, profileId, "New name", "Bio", "Moscow", "https://example.com"),
            CancellationToken.None);

        Assert.Equal(UpdateMemberProfileStatus.Updated, result.Status);
        Assert.Equal("New name", repository.Member.DisplayName);
        Assert.Equal(1, repository.SaveCount);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeMemberProfileRepository : IMemberProfileRepository
    {
        public string? ExistingLogin { get; init; }

        public MemberProfile? Member { get; init; }

        public bool TryAddSucceeds { get; init; } = true;

        public MemberProfile? AddedMember { get; private set; }

        public bool FindByIdCalled { get; private set; }

        public int SaveCount { get; private set; }

        public Task<bool> TryAddAsync(MemberProfile member, CancellationToken cancellationToken)
        {
            AddedMember = member;
            SaveCount++;
            return Task.FromResult(TryAddSucceeds);
        }

        public Task<bool> LoginExistsAsync(string normalizedLogin, CancellationToken cancellationToken) =>
            Task.FromResult(normalizedLogin == ExistingLogin);

        public Task<MemberProfile?> FindByIdAsync(
            Guid id,
            bool trackChanges,
            CancellationToken cancellationToken)
        {
            FindByIdCalled = true;
            return Task.FromResult(Member?.Id == id ? Member : null);
        }

        public Task<MemberProfile?> FindByLoginAsync(
            string normalizedLogin,
            CancellationToken cancellationToken) =>
            Task.FromResult(Member?.Login == normalizedLogin ? Member : null);

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveCount++;
            return Task.CompletedTask;
        }
    }
}
