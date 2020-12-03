using SpaceForum.Domain.Members;

namespace SpaceForum.Application.Members;

public interface IMemberProfileRepository
{
    Task<bool> TryAddAsync(MemberProfile member, CancellationToken cancellationToken);

    Task<bool> LoginExistsAsync(string normalizedLogin, CancellationToken cancellationToken);

    Task<MemberProfile?> FindByIdAsync(Guid id, bool trackChanges, CancellationToken cancellationToken);

    Task<MemberProfile?> FindByLoginAsync(string normalizedLogin, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
