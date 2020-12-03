using SpaceForum.Application.Discussions;

namespace SpaceForum.Application.Tests.Discussions;

public sealed class TopicPaginationTests
{
    [Theory]
    [InlineData(-1, 1)]
    [InlineData(1, 1)]
    [InlineData(50, 1)]
    [InlineData(51, 2)]
    [InlineData(500, 10)]
    [InlineData(501, 11)]
    public void PageForPostReturnsStablePage(int postNumber, int expectedPage)
    {
        Assert.Equal(expectedPage, TopicPagination.PageForPost(postNumber));
    }
}
