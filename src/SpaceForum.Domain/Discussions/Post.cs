using SpaceForum.Domain;

namespace SpaceForum.Domain.Discussions;

public sealed class Post
{
    public const int BodyMaxLength = 20_000;

    private Post()
    {
    }

    private Post(
        Guid id,
        Guid topicId,
        Guid authorId,
        int number,
        string body,
        Guid? replyToPostId,
        DateTimeOffset createdAt)
    {
        Id = id;
        TopicId = topicId;
        AuthorId = authorId;
        Number = number;
        Body = body;
        ReplyToPostId = replyToPostId;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
        Version = 1;
    }

    public Guid Id { get; private set; }

    public Guid TopicId { get; private set; }

    public Guid AuthorId { get; private set; }

    public int Number { get; private set; }

    public string Body { get; private set; } = string.Empty;

    public Guid? ReplyToPostId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public long Version { get; private set; }

    public static Post Create(
        Guid id,
        Guid topicId,
        Guid authorId,
        int number,
        string body,
        DateTimeOffset createdAt,
        Guid? replyToPostId = null)
    {
        if (id == Guid.Empty || topicId == Guid.Empty || authorId == Guid.Empty)
        {
            throw new DomainRuleViolationException("Post, topic, and author IDs are required.");
        }

        if (number < 1)
        {
            throw new DomainRuleViolationException("Post number must be positive.");
        }

        var normalizedBody = (body ?? string.Empty).Trim();
        if (normalizedBody.Length is < 5 or > BodyMaxLength)
        {
            throw new DomainRuleViolationException($"Post body must contain between 5 and {BodyMaxLength} characters.");
        }

        if (replyToPostId == id)
        {
            throw new DomainRuleViolationException("A post cannot reply to itself.");
        }

        return new(id, topicId, authorId, number, normalizedBody, replyToPostId, createdAt.ToUniversalTime());
    }
}
