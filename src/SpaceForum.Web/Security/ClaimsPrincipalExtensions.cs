using System.Security.Claims;

namespace SpaceForum.Web.Security;

internal static class ClaimsPrincipalExtensions
{
    public static Guid? GetMemberId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var memberId)
            ? memberId
            : null;
}
