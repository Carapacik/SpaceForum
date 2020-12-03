using SpaceForum.Domain;
using SpaceForum.Domain.Discussions;
using SpaceForum.Domain.Forums;
using SpaceForum.Application.Forums;

namespace SpaceForum.Application.Discussions;

public sealed record CreateTopicCommand(Guid ActorMemberId, Guid CategoryId, string Title, string Body);

public enum CreateTopicStatus
{
    Created,
    Invalid,
    Forbidden,
    CategoryNotFound,
}

public sealed record CreateTopicResult(
    CreateTopicStatus Status,
    Guid? TopicId = null,
    TopicRoute? Route = null,
    string? Error = null);

public sealed class CreateTopicHandler(
    IDiscussionRepository discussions,
    IForumCategoryRepository categories,
    IForumPostingAccess postingAccess,
    TimeProvider timeProvider)
{
    public async Task<CreateTopicResult> HandleAsync(
        CreateTopicCommand command,
        CancellationToken cancellationToken)
    {
        if (!await postingAccess.CanPostAsync(command.ActorMemberId, cancellationToken))
        {
            return new(CreateTopicStatus.Forbidden);
        }

        var category = await categories.FindByIdAsync(command.CategoryId, cancellationToken);
        if (category is null)
        {
            return new(CreateTopicStatus.CategoryNotFound);
        }

        if (category.Format is CategoryFormat.Announcement
            && !await postingAccess.CanCreateAnnouncementAsync(command.ActorMemberId, cancellationToken))
        {
            return new(CreateTopicStatus.Forbidden, Error: "Only administrators can create announcements.");
        }

        try
        {
            var now = timeProvider.GetUtcNow();
            var topic = Topic.Create(
                Guid.CreateVersion7(),
                category.Id,
                command.ActorMemberId,
                command.Title,
                now);
            var firstPost = Post.Create(
                Guid.CreateVersion7(),
                topic.Id,
                command.ActorMemberId,
                number: 1,
                command.Body,
                now);
            if (!await discussions.CreateAsync(topic, firstPost, cancellationToken))
            {
                return new(CreateTopicStatus.Invalid, Error: "The topic could not be created.");
            }

            var route = await discussions.GetRouteAsync(topic.Id, cancellationToken);
            return route is null
                ? new(CreateTopicStatus.Invalid, Error: "The topic route could not be created.")
                : new(CreateTopicStatus.Created, topic.Id, route);
        }
        catch (DomainRuleViolationException exception)
        {
            return new(CreateTopicStatus.Invalid, Error: exception.Message);
        }
    }
}
