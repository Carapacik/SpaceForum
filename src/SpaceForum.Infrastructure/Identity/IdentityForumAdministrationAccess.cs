using Microsoft.AspNetCore.Identity;
using SpaceForum.Application.Forums;
using SpaceForum.Application.Security;

namespace SpaceForum.Infrastructure.Identity;

public sealed class IdentityForumAdministrationAccess(UserManager<ApplicationUser> userManager)
    : IForumAdministrationAccess
{
    public async Task<bool> CanAdministerAsync(Guid memberId, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(memberId.ToString());
        return user is not null && await userManager.IsInRoleAsync(user, ForumRoles.Administrator);
    }
}
