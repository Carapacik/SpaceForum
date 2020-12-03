namespace SpaceForum.Application.Discussions;

public sealed class GetDiscussionsHandler(IDiscussionRepository repository)
{
    public Task<IReadOnlyList<TopicListItem>> ListAsync(
        TopicSort sort,
        int take,
        CancellationToken cancellationToken) =>
        repository.ListAsync(sort, Math.Clamp(take, 1, 50), cancellationToken);

    public Task<IReadOnlyList<TopicListItem>> ByCategoryAsync(Guid categoryId, CancellationToken cancellationToken) =>
        repository.ListByCategoryAsync(categoryId, cancellationToken);

    public Task<IReadOnlyList<TopicListItem>> SearchAsync(
        string query,
        int take,
        CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(query)
            ? Task.FromResult<IReadOnlyList<TopicListItem>>([])
            : repository.SearchAsync(query.Trim(), Math.Clamp(take, 1, 50), cancellationToken);

    public Task<TopicRoute?> RouteAsync(Guid topicId, CancellationToken cancellationToken) =>
        repository.GetRouteAsync(topicId, cancellationToken);

    public Task<TopicDetail?> DetailAsync(long topicNumber, int page, CancellationToken cancellationToken) =>
        repository.GetDetailAsync(topicNumber, Math.Max(1, page), TopicPagination.PageSize, cancellationToken);

    public Task<TopicVoteState> VoteAsync(
        Guid topicId,
        Guid? memberId,
        CancellationToken cancellationToken) =>
        repository.GetVoteAsync(topicId, memberId, cancellationToken);
}
