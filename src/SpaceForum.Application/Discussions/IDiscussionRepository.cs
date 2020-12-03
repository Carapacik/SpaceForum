using SpaceForum.Domain.Discussions;

namespace SpaceForum.Application.Discussions;

public interface IDiscussionRepository
{
    Task<bool> CreateAsync(Topic topic, Post firstPost, CancellationToken cancellationToken);

    Task<Topic?> FindTopicAsync(Guid topicId, bool trackChanges, CancellationToken cancellationToken);

    Task<int> GetNextPostNumberAsync(Guid topicId, CancellationToken cancellationToken);
    Task<int> GetLastPostNumberAsync(Guid topicId, CancellationToken cancellationToken);

    Task<bool> AddReplyAsync(Post post, CancellationToken cancellationToken);

    Task<IReadOnlyList<TopicListItem>> ListAsync(TopicSort sort, int take, CancellationToken cancellationToken);

    Task<IReadOnlyList<TopicListItem>> ListByCategoryAsync(Guid categoryId, CancellationToken cancellationToken);

    Task<IReadOnlyList<TopicListItem>> SearchAsync(string query, int take, CancellationToken cancellationToken);

    Task<TopicRoute?> GetRouteAsync(Guid topicId, CancellationToken cancellationToken);

    Task<TopicDetail?> GetDetailAsync(long topicNumber, int page, int pageSize, CancellationToken cancellationToken);

    Task<TopicVoteState> GetVoteAsync(Guid topicId, Guid? memberId, CancellationToken cancellationToken);

    Task<TopicVoteState?> SetVoteAsync(
        Guid topicId,
        Guid memberId,
        int value,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken);

    Task<bool> SetClosedAsync(Guid topicId, bool isClosed, CancellationToken cancellationToken);

    Task<bool> DeleteTopicAsync(Guid topicId, CancellationToken cancellationToken);

    Task<bool> DeleteReplyAsync(Guid postId, CancellationToken cancellationToken);
}
