using Microsoft.AspNetCore.Identity;

namespace SpaceForum.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public ApplicationUser()
    {
        Id = Guid.CreateVersion7();
    }
}
