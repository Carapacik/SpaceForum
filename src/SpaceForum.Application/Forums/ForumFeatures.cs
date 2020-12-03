using SpaceForum.Application.Security;
using SpaceForum.Application.Discussions;
using SpaceForum.Domain.Discussions;

namespace SpaceForum.Application.Forums;

public sealed record PostAccess(Guid PostId, Guid TopicId, Guid AuthorId, int Number, bool IsHidden);
public sealed record PostRevisionView(Guid Id, string PreviousBody, string Body, string EditorLogin, DateTimeOffset EditedAt);
public sealed record NotificationView(Guid Id, string Type, string ActorDisplayName, long? TopicNumber, string? TopicSlug, int? PostNumber, DateTimeOffset CreatedAt, bool IsRead);
public sealed record ReactionView(string Reaction, int Count, bool ReactedByActor);
public sealed record ReportView(Guid Id, Guid PostId, long TopicNumber, string TopicSlug, int PostNumber, string ReporterLogin, string Reason, string? Details, string Status, DateTimeOffset CreatedAt);
public sealed record BookmarkView(Guid PostId, long TopicNumber, string TopicSlug, int PostNumber, string TopicTitle, string AuthorDisplayName, DateTimeOffset CreatedAt);
public sealed record TagView(Guid Id, string Name, string Slug, string Description, string Color, int Position);
public sealed record TopicFeatureState(bool IsSticky, string? Subscription, IReadOnlyList<TagView> Tags);

public interface IForumFeatureRepository
{
    Task<PostAccess?> GetPostAccessAsync(Guid postId, CancellationToken cancellationToken);
    Task<bool> EditPostAsync(Guid postId, Guid editorId, string body, DateTimeOffset editedAt, CancellationToken cancellationToken);
    Task<IReadOnlyList<PostRevisionView>> GetPostRevisionsAsync(Guid postId, CancellationToken cancellationToken);
    Task<bool> SetPostHiddenAsync(Guid postId, Guid actorId, bool hidden, DateTimeOffset changedAt, CancellationToken cancellationToken);
    Task<string?> SetSubscriptionAsync(Guid topicId, Guid memberId, string? state, int lastReadPostNumber, DateTimeOffset changedAt, CancellationToken cancellationToken);
    Task MarkReadAsync(Guid topicId, Guid memberId, int postNumber, DateTimeOffset changedAt, CancellationToken cancellationToken);
    Task<IReadOnlyList<NotificationView>> GetNotificationsAsync(Guid memberId, int take, CancellationToken cancellationToken);
    Task<int> GetUnreadNotificationCountAsync(Guid memberId, CancellationToken cancellationToken);
    Task MarkNotificationsReadAsync(Guid memberId, DateTimeOffset readAt, CancellationToken cancellationToken);
    Task<IReadOnlyList<ReactionView>> GetReactionsAsync(Guid postId, Guid? actorId, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<ReactionView>>> GetTopicReactionsAsync(Guid topicId, Guid? actorId, CancellationToken cancellationToken);
    Task<bool> ToggleReactionAsync(Guid postId, Guid memberId, string reaction, DateTimeOffset changedAt, CancellationToken cancellationToken);
    Task<bool> CreateReportAsync(Guid postId, Guid reporterId, string reason, string? details, DateTimeOffset createdAt, CancellationToken cancellationToken);
    Task<IReadOnlyList<ReportView>> GetReportsAsync(CancellationToken cancellationToken);
    Task<bool> SetReportStatusAsync(Guid reportId, Guid actorId, string status, DateTimeOffset changedAt, CancellationToken cancellationToken);
    Task<bool> ToggleBookmarkAsync(Guid postId, Guid memberId, DateTimeOffset changedAt, CancellationToken cancellationToken);
    Task<IReadOnlyList<BookmarkView>> GetBookmarksAsync(Guid memberId, CancellationToken cancellationToken);
    Task<TopicFeatureState> GetTopicFeaturesAsync(Guid topicId, Guid? memberId, CancellationToken cancellationToken);
    Task<IReadOnlyList<TagView>> GetTagsAsync(CancellationToken cancellationToken);
    Task<bool> CreateTagAsync(TagView tag, CancellationToken cancellationToken);
    Task<bool> DeleteTagAsync(Guid tagId, CancellationToken cancellationToken);
    Task<bool> UpdateTopicAsync(Guid topicId, string title, Guid categoryId, bool isSticky, IReadOnlyCollection<Guid> tagIds, CancellationToken cancellationToken);
}

public enum ForumFeatureStatus { Updated, Created, NotFound, Forbidden, Invalid, Conflict }
public sealed record ForumFeatureResult(ForumFeatureStatus Status, string? Error = null)
{
    public bool Succeeded => Status is ForumFeatureStatus.Updated or ForumFeatureStatus.Created;
}

public sealed class ForumFeatureService(
    IForumFeatureRepository repository,
    IForumModerationAccess moderationAccess,
    ISecurityAuditWriter audit,
    TimeProvider timeProvider)
{
    private static readonly HashSet<string> AllowedReactions = ["like", "love", "laugh", "insightful"];
    private static readonly HashSet<string> AllowedReportReasons = ["spam", "abuse", "off-topic", "other"];

    public async Task<ForumFeatureResult> EditPostAsync(Guid actorId, Guid postId, string body, CancellationToken cancellationToken)
    {
        var access = await repository.GetPostAccessAsync(postId, cancellationToken);
        if (access is null) return new(ForumFeatureStatus.NotFound);
        if (access.AuthorId != actorId && !await moderationAccess.IsAdministratorAsync(actorId, cancellationToken)) return new(ForumFeatureStatus.Forbidden);
        var normalized = (body ?? string.Empty).Trim();
        if (normalized.Length is < 5 or > Post.BodyMaxLength) return new(ForumFeatureStatus.Invalid, "Post length is invalid.");
        if (!await repository.EditPostAsync(postId, actorId, normalized, timeProvider.GetUtcNow(), cancellationToken)) return new(ForumFeatureStatus.Conflict);
        await audit.WriteAsync(new("forum.post.edited", actorId, postId.ToString(), true), cancellationToken);
        return new(ForumFeatureStatus.Updated);
    }

    public async Task<ForumFeatureResult> SetPostHiddenAsync(Guid actorId, Guid postId, bool hidden, CancellationToken cancellationToken)
    {
        if (!await moderationAccess.IsAdministratorAsync(actorId, cancellationToken)) return new(ForumFeatureStatus.Forbidden);
        var access = await repository.GetPostAccessAsync(postId, cancellationToken);
        if (access is null || access.Number == 1) return new(ForumFeatureStatus.Invalid);
        if (!await repository.SetPostHiddenAsync(postId, actorId, hidden, timeProvider.GetUtcNow(), cancellationToken)) return new(ForumFeatureStatus.Conflict);
        await audit.WriteAsync(new(hidden ? "forum.post.hidden" : "forum.post.restored", actorId, postId.ToString(), true), cancellationToken);
        return new(ForumFeatureStatus.Updated);
    }

    public async Task<ForumFeatureResult> ToggleReactionAsync(Guid actorId, Guid postId, string reaction, CancellationToken cancellationToken)
    {
        if (!AllowedReactions.Contains(reaction)) return new(ForumFeatureStatus.Invalid);
        return await repository.ToggleReactionAsync(postId, actorId, reaction, timeProvider.GetUtcNow(), cancellationToken)
            ? new(ForumFeatureStatus.Updated)
            : new(ForumFeatureStatus.NotFound);
    }

    public async Task<ForumFeatureResult> ReportAsync(Guid actorId, Guid postId, string reason, string? details, CancellationToken cancellationToken)
    {
        if (!AllowedReportReasons.Contains(reason) || details?.Length > 1000) return new(ForumFeatureStatus.Invalid);
        var access = await repository.GetPostAccessAsync(postId, cancellationToken);
        if (access is null) return new(ForumFeatureStatus.NotFound);
        if (access.AuthorId == actorId) return new(ForumFeatureStatus.Forbidden, "You cannot report your own post.");
        return await repository.CreateReportAsync(postId, actorId, reason, details?.Trim(), timeProvider.GetUtcNow(), cancellationToken)
            ? new(ForumFeatureStatus.Created)
            : new(ForumFeatureStatus.Conflict, "This post is already reported by you.");
    }

    public async Task<ForumFeatureResult> ResolveReportAsync(Guid actorId, Guid reportId, string status, CancellationToken cancellationToken)
    {
        if (!await moderationAccess.IsAdministratorAsync(actorId, cancellationToken)) return new(ForumFeatureStatus.Forbidden);
        if (status is not ("Resolved" or "Dismissed")) return new(ForumFeatureStatus.Invalid);
        return await repository.SetReportStatusAsync(reportId, actorId, status, timeProvider.GetUtcNow(), cancellationToken)
            ? new(ForumFeatureStatus.Updated)
            : new(ForumFeatureStatus.NotFound);
    }
}
