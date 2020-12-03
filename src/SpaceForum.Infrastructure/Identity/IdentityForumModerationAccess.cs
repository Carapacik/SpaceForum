using Microsoft.AspNetCore.Identity;
using SpaceForum.Application.Discussions;
using SpaceForum.Application.Security;

namespace SpaceForum.Infrastructure.Identity;

public sealed class IdentityForumModerationAccess(UserManager<ApplicationUser> userManager) : IForumModerationAccess
{
    public async Task<bool> IsAdministratorAsync(Guid memberId, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(memberId.ToString());
        return user is not null && await userManager.IsInRoleAsync(user, ForumRoles.Administrator);
    }
}
