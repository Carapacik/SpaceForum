using Microsoft.AspNetCore.Http;
using SpaceForum.Web.Security;

namespace SpaceForum.IntegrationTests.Security;

public sealed class AuthenticationRateLimitPolicyTests
{
    [Theory]
    [InlineData("/account/register", "register", 5, 15)]
    [InlineData("/account/login", "login", 10, 5)]
    [InlineData("/account/loginwith2fa", "login", 10, 5)]
    [InlineData("/account/passkeyrequestoptions", "passkey-request", 20, 5)]
    [InlineData("/account/forgotpassword", "account-recovery", 5, 15)]
    [InlineData("/account/resetpassword", "account-recovery", 5, 15)]
    public void PostToSensitiveIdentityPathHasAnExplicitLimit(
        string path,
        string operation,
        int permitLimit,
        int windowMinutes)
    {
        var context = CreateContext(HttpMethods.Post, path);

        var rule = AuthenticationRateLimitPolicy.Match(context.Request);

        Assert.NotNull(rule);
        Assert.Equal(operation, rule.Operation);
        Assert.Equal(permitLimit, rule.PermitLimit);
        Assert.Equal(TimeSpan.FromMinutes(windowMinutes), rule.Window);
    }

    [Theory]
    [InlineData("GET", "/account/login")]
    [InlineData("POST", "/")]
    [InlineData("POST", "/account/manage")]
    public void OtherRequestsDoNotConsumeAnAuthenticationLimit(string method, string path)
    {
        var context = CreateContext(method, path);

        Assert.Null(AuthenticationRateLimitPolicy.Match(context.Request));
    }

    private static DefaultHttpContext CreateContext(string method, string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        return context;
    }
}
