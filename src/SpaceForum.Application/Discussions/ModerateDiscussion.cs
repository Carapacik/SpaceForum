using SpaceForum.Application.Security;

namespace SpaceForum.Application.Discussions;

public enum ModerateDiscussionStatus
{
    Updated,
    Deleted,
    NotFound,
    Forbidden,
    Invalid,
    Conflict,
}

public sealed record ModerateDiscussionResult(ModerateDiscussionStatus Status, string? Error = null)
{
    public bool Succeeded => Status is ModerateDiscussionStatus.Updated or ModerateDiscussionStatus.Deleted;
}

public sealed class ModerateDiscussionHandler(
    IDiscussionRepository repository,
    IForumModerationAccess access,
    ISecurityAuditWriter audit)
{
    public async Task<ModerateDiscussionResult> SetClosedAsync(
        Guid actorId,
        Guid topicId,
        bool isClosed,
        CancellationToken cancellationToken)
    {
        var topic = await repository.FindTopicAsync(topicId, trackChanges: false, cancellationToken);
        if (topic is null)
        {
            return new(ModerateDiscussionStatus.NotFound, "The topic was not found.");
        }

        var isAdministrator = await access.IsAdministratorAsync(actorId, cancellationToken);
        if (topic.AuthorId != actorId && !isAdministrator)
        {
            return new(ModerateDiscussionStatus.Forbidden, "Only the topic creator or an administrator can change its state.");
        }

        if (!await repository.SetClosedAsync(topicId, isClosed, cancellationToken))
        {
            return new(ModerateDiscussionStatus.Conflict, "The topic changed while the request was being processed.");
        }

        await audit.WriteAsync(
            new(
                isClosed ? SecurityEventTypes.TopicClosed : SecurityEventTypes.TopicReopened,
                actorId,
                topicId.ToString(),
                Succeeded: true),
            cancellationToken);
        return new(ModerateDiscussionStatus.Updated);
    }

    public async Task<ModerateDiscussionResult> DeleteTopicAsync(
        Guid actorId,
        Guid topicId,
        CancellationToken cancellationToken)
    {
        if (!await access.IsAdministratorAsync(actorId, cancellationToken))
        {
            return new(ModerateDiscussionStatus.Forbidden, "Only an administrator can delete a topic.");
        }

        if (!await repository.DeleteTopicAsync(topicId, cancellationToken))
        {
            return new(ModerateDiscussionStatus.NotFound, "The topic was not found.");
        }

        await audit.WriteAsync(
            new(SecurityEventTypes.TopicDeleted, actorId, topicId.ToString(), Succeeded: true),
            cancellationToken);
        return new(ModerateDiscussionStatus.Deleted);
    }

    public async Task<ModerateDiscussionResult> DeletePostAsync(
        Guid actorId,
        Guid postId,
        CancellationToken cancellationToken)
    {
        if (!await access.IsAdministratorAsync(actorId, cancellationToken))
        {
            return new(ModerateDiscussionStatus.Forbidden, "Only an administrator can delete a post.");
        }

        if (!await repository.DeleteReplyAsync(postId, cancellationToken))
        {
            return new(ModerateDiscussionStatus.Invalid, "The post was not found or is the opening post.");
        }

        await audit.WriteAsync(
            new(SecurityEventTypes.PostDeleted, actorId, postId.ToString(), Succeeded: true),
            cancellationToken);
        return new(ModerateDiscussionStatus.Deleted);
    }
}
