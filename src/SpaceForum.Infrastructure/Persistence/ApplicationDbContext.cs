using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SpaceForum.Infrastructure.Identity;

namespace SpaceForum.Infrastructure.Persistence;

// EF Core is intentionally limited to the framework-supported Identity store.
// SpaceForum application data and schema migrations use parameterised Npgsql SQL.
public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
}
