using SpaceForum.Domain.Forums;

namespace SpaceForum.Application.Forums;

public interface IForumCategoryRepository
{
    Task<IReadOnlyList<ForumCategory>> ListAsync(CancellationToken cancellationToken);

    Task<ForumCategory?> FindBySlugAsync(string normalizedSlug, CancellationToken cancellationToken);

    Task<ForumCategory?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> SlugExistsAsync(string normalizedSlug, CancellationToken cancellationToken);

    Task<bool> TryAddAsync(ForumCategory category, CancellationToken cancellationToken);

    Task<bool> TryDeleteEmptyAsync(Guid id, CancellationToken cancellationToken);
}
