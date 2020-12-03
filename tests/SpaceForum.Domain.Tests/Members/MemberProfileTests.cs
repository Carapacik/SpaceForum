using SpaceForum.Domain;
using SpaceForum.Domain.Members;

namespace SpaceForum.Domain.Tests.Members;

public sealed class MemberProfileTests
{
    private static readonly DateTimeOffset CreatedAt = new(2020, 12, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreateNormalizesLoginAndDisplayName()
    {
        var member = MemberProfile.Create(Guid.CreateVersion7(), "  SpaceUser  ", "  Alex Morgan  ", CreatedAt);

        Assert.Equal("spaceuser", member.Login);
        Assert.Equal("Alex Morgan", member.DisplayName);
        Assert.Equal(CreatedAt, member.CreatedAt);
        Assert.Equal(1, member.Version);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("-starts-with-hyphen")]
    [InlineData("contains space")]
    [InlineData("δοκιμή")]
    [InlineData("user@example")]
    [InlineData("user_name")]
    [InlineData("user-name")]
    public void CreateRejectsUnsafeLogins(string login)
    {
        var action = () => MemberProfile.Create(Guid.CreateVersion7(), login, "Member", CreatedAt);

        Assert.Throws<DomainRuleViolationException>(action);
    }

    [Fact]
    public void UpdateNormalizesOptionalFieldsAndIncrementsVersion()
    {
        var member = MemberProfile.Create(Guid.CreateVersion7(), "spaceuser", "Member", CreatedAt);

        member.Update(
            "  New name ",
            "  Biography  ",
            "  Moscow  ",
            "https://example.com/about",
            CreatedAt.AddMinutes(5));

        Assert.Equal("New name", member.DisplayName);
        Assert.Equal("Biography", member.Biography);
        Assert.Equal("Moscow", member.Location);
        Assert.Equal("https://example.com/about", member.Website);
        Assert.Equal(2, member.Version);
        Assert.Equal(CreatedAt.AddMinutes(5), member.UpdatedAt);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://example.com/file")]
    [InlineData("example.com")]
    public void UpdateRejectsNonWebUrls(string website)
    {
        var member = MemberProfile.Create(Guid.CreateVersion7(), "spaceuser", "Member", CreatedAt);

        var action = () => member.Update("Member", null, null, website, CreatedAt);

        Assert.Throws<DomainRuleViolationException>(action);
    }

    [Fact]
    public void UpdateRejectsAnOlderTimestamp()
    {
        var member = MemberProfile.Create(Guid.CreateVersion7(), "spaceuser", "Member", CreatedAt);

        var action = () => member.Update("Member", null, null, null, CreatedAt.AddSeconds(-1));

        Assert.Throws<DomainRuleViolationException>(action);
    }
}
