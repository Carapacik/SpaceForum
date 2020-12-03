namespace SpaceForum.Application.Forums;

public interface IForumAdministrationAccess
{
    Task<bool> CanAdministerAsync(Guid memberId, CancellationToken cancellationToken);
}
