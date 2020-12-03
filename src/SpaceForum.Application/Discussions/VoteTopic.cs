namespace SpaceForum.Application.Discussions;

public sealed record VoteTopicCommand(Guid ActorMemberId, Guid TopicId, int Value);

public sealed record VoteTopicResult(bool Succeeded, TopicVoteState? State = null);

public sealed class VoteTopicHandler(
    IDiscussionRepository discussions,
    IForumPostingAccess postingAccess,
    TimeProvider timeProvider)
{
    public async Task<VoteTopicResult> HandleAsync(
        VoteTopicCommand command,
        CancellationToken cancellationToken)
    {
        if (command.Value is not (-1 or 1)
            || !await postingAccess.CanPostAsync(command.ActorMemberId, cancellationToken))
        {
            return new(false);
        }

        var state = await discussions.SetVoteAsync(
            command.TopicId,
            command.ActorMemberId,
            command.Value,
            timeProvider.GetUtcNow(),
            cancellationToken);
        return new(state is not null, state);
    }
}
