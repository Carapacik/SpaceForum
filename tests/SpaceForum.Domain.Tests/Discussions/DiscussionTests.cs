using SpaceForum.Domain;
using SpaceForum.Domain.Discussions;

namespace SpaceForum.Domain.Tests.Discussions;

public sealed class DiscussionTests
{
    private static readonly DateTimeOffset Now = new(2020, 12, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TopicRecordsRepliesAndLatestActivity()
    {
        var topic = Topic.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "A useful topic", Now);

        topic.RecordReply(Now.AddMinutes(5));

        Assert.Equal(1, topic.ReplyCount);
        Assert.Equal(Now.AddMinutes(5), topic.LastActivityAt);
        Assert.Equal(2, topic.Version);
    }

    [Fact]
    public void TopicRejectsOutOfOrderActivity()
    {
        var topic = Topic.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "A useful topic", Now);

        var action = () => topic.RecordReply(Now.AddSeconds(-1));

        Assert.Throws<DomainRuleViolationException>(action);
    }

    [Fact]
    public void PostTrimsPlainTextBody()
    {
        var post = Post.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 1, "  Hello forum  ", Now);

        Assert.Equal("Hello forum", post.Body);
        Assert.Equal(1, post.Number);
    }

    [Fact]
    public void PostKeepsRelationalReplyTarget()
    {
        var sourceId = Guid.CreateVersion7();
        var post = Post.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 2, "A linked reply", Now, sourceId);

        Assert.Equal(sourceId, post.ReplyToPostId);
    }

    [Theory]
    [InlineData(0, "Valid message")]
    [InlineData(1, "no")]
    public void PostRejectsInvalidContent(int number, string body)
    {
        var action = () => Post.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), number, body, Now);

        Assert.Throws<DomainRuleViolationException>(action);
    }

    [Theory]
    [InlineData("SpaceForum 2.0 released!", "spaceforum-2-0-released")]
    [InlineData("  New forum: .NET and PostgreSQL  ", "new-forum-net-and-postgresql")]
    [InlineData("***", "topic")]
    public void TopicSlugCreatesStableLowercasePaths(string title, string expected)
    {
        Assert.Equal(expected, TopicSlug.Create(title));
    }
}
