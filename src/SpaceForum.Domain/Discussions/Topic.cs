using SpaceForum.Domain;

namespace SpaceForum.Domain.Discussions;

public sealed class Topic
{
    public const int TitleMaxLength = 160;

    private Topic()
    {
    }

    private Topic(
        Guid id,
        Guid categoryId,
        Guid authorId,
        string title,
        DateTimeOffset createdAt)
    {
        Id = id;
        CategoryId = categoryId;
        AuthorId = authorId;
        Title = title;
        CreatedAt = createdAt;
        LastActivityAt = createdAt;
        Version = 1;
    }

    public Guid Id { get; private set; }

    public Guid CategoryId { get; private set; }

    public Guid AuthorId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset LastActivityAt { get; private set; }

    public int ReplyCount { get; private set; }

    public bool IsClosed { get; private set; }

    public long Version { get; private set; }

    public static Topic Create(
        Guid id,
        Guid categoryId,
        Guid authorId,
        string title,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty || categoryId == Guid.Empty || authorId == Guid.Empty)
        {
            throw new DomainRuleViolationException("Topic, category, and author IDs are required.");
        }

        var normalizedTitle = (title ?? string.Empty).Trim();
        if (normalizedTitle.Length is < 5 or > TitleMaxLength)
        {
            throw new DomainRuleViolationException($"Topic title must contain between 5 and {TitleMaxLength} characters.");
        }

        return new(id, categoryId, authorId, normalizedTitle, createdAt.ToUniversalTime());
    }

    internal static Topic Restore(
        Guid id,
        Guid categoryId,
        Guid authorId,
        string title,
        DateTimeOffset createdAt,
        DateTimeOffset lastActivityAt,
        int replyCount,
        bool isClosed,
        long version)
    {
        var topic = Create(id, categoryId, authorId, title, createdAt);
        var utcLastActivityAt = lastActivityAt.ToUniversalTime();
        if (utcLastActivityAt < topic.CreatedAt || replyCount < 0 || version < 1)
        {
            throw new DomainRuleViolationException("Persisted topic state is invalid.");
        }

        topic.LastActivityAt = utcLastActivityAt;
        topic.ReplyCount = replyCount;
        topic.IsClosed = isClosed;
        topic.Version = version;
        return topic;
    }

    public void RecordReply(DateTimeOffset repliedAt)
    {
        if (IsClosed)
        {
            throw new DomainRuleViolationException("A closed topic cannot receive replies.");
        }

        var utcRepliedAt = repliedAt.ToUniversalTime();
        if (utcRepliedAt < LastActivityAt)
        {
            throw new DomainRuleViolationException("A reply cannot be older than the topic's latest activity.");
        }

        ReplyCount++;
        LastActivityAt = utcRepliedAt;
        Version++;
    }
}
