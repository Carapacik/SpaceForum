using Microsoft.AspNetCore.Identity;
using SpaceForum.Application.Discussions;
using SpaceForum.Application.Security;

namespace SpaceForum.Infrastructure.Identity;

public sealed class IdentityForumPostingAccess(UserManager<ApplicationUser> userManager) : IForumPostingAccess
{
    public async Task<bool> CanPostAsync(Guid memberId, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(memberId.ToString());
        return user is not null
            && await userManager.IsEmailConfirmedAsync(user)
            && !await userManager.IsLockedOutAsync(user);
    }

    public async Task<bool> CanCreateAnnouncementAsync(Guid memberId, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(memberId.ToString());
        return user is not null && await userManager.IsInRoleAsync(user, ForumRoles.Administrator);
    }
}
