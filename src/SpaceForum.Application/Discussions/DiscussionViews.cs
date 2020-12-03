namespace SpaceForum.Application.Discussions;

public sealed record TopicListItem(
    Guid Id,
    long Number,
    string Slug,
    string Title,
    string CategoryName,
    string CategorySlug,
    string AuthorLogin,
    string AuthorDisplayName,
    int ReplyCount,
    int Score,
    DateTimeOffset LastActivityAt);

public enum TopicSort
{
    Latest,
    Newest,
    Oldest,
    Top,
    HighestRated,
    LowestRated,
}

public sealed record TopicRoute(long Number, string Slug)
{
    public string Path => $"/d/{Number}-{Slug}";
}

public sealed record PostView(
    Guid Id,
    int Number,
    string Body,
    Guid AuthorId,
    string AuthorLogin,
    string AuthorDisplayName,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid? ReplyToPostId,
    int? ReplyToPostNumber,
    bool IsHidden,
    int RevisionCount);

public sealed record TopicDetail(
    Guid Id,
    long Number,
    string Slug,
    string Title,
    Guid AuthorId,
    Guid CategoryId,
    string CategoryName,
    string CategorySlug,
    bool IsClosed,
    int Score,
    int Page,
    int PageSize,
    int TotalPosts,
    int LastPostNumber,
    IReadOnlyList<PostView> Posts)
{
    public int TotalPages => Math.Max(1, (LastPostNumber + PageSize - 1) / PageSize);
}

public static class TopicPagination
{
    public const int PageSize = 50;

    public static int PageForPost(int postNumber) =>
        Math.Max(1, (Math.Max(1, postNumber) - 1) / PageSize + 1);
}

public sealed record TopicVoteState(int Score, int? Vote);
