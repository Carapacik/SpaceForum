namespace SpaceForum.Application.Discussions;

public interface IForumPostingAccess
{
    Task<bool> CanPostAsync(Guid memberId, CancellationToken cancellationToken);

    Task<bool> CanCreateAnnouncementAsync(Guid memberId, CancellationToken cancellationToken);
}
