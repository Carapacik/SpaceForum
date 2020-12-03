using SpaceForum.Domain;
using SpaceForum.Domain.Forums;

namespace SpaceForum.Domain.Tests.Forums;

public sealed class ForumCategoryTests
{
    private static readonly DateTimeOffset Now = new(2020, 12, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreateNormalizesTextAndSlug()
    {
        var category = ForumCategory.Create(
            Guid.CreateVersion7(),
            "  C# and .NET  ",
            "  DOTNET  ",
            "  Questions about modern .NET.  ",
            CategoryFormat.QuestionAndAnswer,
            null,
            10,
            Now);

        Assert.Equal("C# and .NET", category.Name);
        Assert.Equal("dotnet", category.Slug);
        Assert.Equal("Questions about modern .NET.", category.Description);
        Assert.Equal(1, category.Version);
    }

    [Theory]
    [InlineData("-leading")]
    [InlineData("trailing-")]
    [InlineData("contains space")]
    [InlineData("under_score")]
    [InlineData("δοκιμή")]
    public void CreateRejectsUnsafeSlugs(string slug)
    {
        var action = () => ForumCategory.Create(
            Guid.CreateVersion7(), "Category", slug, "Description", CategoryFormat.Discussion, null, 0, Now);

        Assert.Throws<DomainRuleViolationException>(action);
    }

    [Fact]
    public void CreateRejectsSelfParent()
    {
        var id = Guid.CreateVersion7();

        var action = () => ForumCategory.Create(
            id, "Category", "category", "Description", CategoryFormat.Discussion, id, 0, Now);

        Assert.Throws<DomainRuleViolationException>(action);
    }

    [Fact]
    public void CreateRejectsNegativePosition()
    {
        var action = () => ForumCategory.Create(
            Guid.CreateVersion7(), "Category", "category", "Description", CategoryFormat.Discussion, null, -1, Now);

        Assert.Throws<DomainRuleViolationException>(action);
    }
}
