using SpaceForum.Domain;
using SpaceForum.Domain.Discussions;

namespace SpaceForum.Application.Discussions;

public sealed record CreateReplyCommand(Guid ActorMemberId, Guid TopicId, string Body, Guid? ReplyToPostId = null);

public enum CreateReplyStatus
{
    Created,
    Invalid,
    Forbidden,
    TopicNotFound,
    Closed,
}

public sealed record CreateReplyResult(CreateReplyStatus Status, int? PostNumber = null, string? Error = null);

public sealed class CreateReplyHandler(
    IDiscussionRepository discussions,
    IForumPostingAccess postingAccess,
    TimeProvider timeProvider)
{
    public async Task<CreateReplyResult> HandleAsync(
        CreateReplyCommand command,
        CancellationToken cancellationToken)
    {
        if (!await postingAccess.CanPostAsync(command.ActorMemberId, cancellationToken))
        {
            return new(CreateReplyStatus.Forbidden);
        }

        var topic = await discussions.FindTopicAsync(command.TopicId, trackChanges: true, cancellationToken);
        if (topic is null)
        {
            return new(CreateReplyStatus.TopicNotFound);
        }

        if (topic.IsClosed)
        {
            return new(CreateReplyStatus.Closed);
        }

        try
        {
            var now = timeProvider.GetUtcNow();
            var number = await discussions.GetNextPostNumberAsync(topic.Id, cancellationToken);
            var post = Post.Create(
                Guid.CreateVersion7(),
                topic.Id,
                command.ActorMemberId,
                number,
                command.Body,
                now,
                command.ReplyToPostId);
            topic.RecordReply(now);
            if (!await discussions.AddReplyAsync(post, cancellationToken))
            {
                return new(CreateReplyStatus.Invalid, Error: "The reply could not be created. Please retry.");
            }

            return new(CreateReplyStatus.Created, number);
        }
        catch (DomainRuleViolationException exception)
        {
            return new(CreateReplyStatus.Invalid, Error: exception.Message);
        }
    }
}
