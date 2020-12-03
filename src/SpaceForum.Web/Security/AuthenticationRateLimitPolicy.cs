using System.Threading.RateLimiting;

namespace SpaceForum.Web.Security;

public sealed record AuthenticationRateLimitRule(string Operation, int PermitLimit, TimeSpan Window);

public static class AuthenticationRateLimitPolicy
{
    public static AuthenticationRateLimitRule? Match(HttpRequest request)
    {
        if (!HttpMethods.IsPost(request.Method))
        {
            return null;
        }

        var path = request.Path;
        if (path.Equals("/account/register", StringComparison.OrdinalIgnoreCase))
        {
            return new("register", 5, TimeSpan.FromMinutes(15));
        }

        if (path.Equals("/account/login", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/account/loginwith2fa", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/account/loginwithrecoverycode", StringComparison.OrdinalIgnoreCase))
        {
            return new("login", 10, TimeSpan.FromMinutes(5));
        }

        if (path.Equals("/account/passkeyrequestoptions", StringComparison.OrdinalIgnoreCase))
        {
            return new("passkey-request", 20, TimeSpan.FromMinutes(5));
        }

        if (path.Equals("/account/forgotpassword", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/account/resetpassword", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/account/resendemailconfirmation", StringComparison.OrdinalIgnoreCase))
        {
            return new("account-recovery", 5, TimeSpan.FromMinutes(15));
        }

        return null;
    }

    public static RateLimitPartition<string> CreatePartition(HttpContext context)
    {
        var rule = Match(context.Request);
        if (rule is null)
        {
            return RateLimitPartition.GetNoLimiter("unlimited");
        }

        var clientAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(
            $"{rule.Operation}:{clientAddress}",
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = rule.PermitLimit,
                QueueLimit = 0,
                Window = rule.Window,
            });
    }
}
