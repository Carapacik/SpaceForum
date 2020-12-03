namespace SpaceForum.Application.Discussions;

public interface IForumModerationAccess
{
    Task<bool> IsAdministratorAsync(Guid memberId, CancellationToken cancellationToken);
}
