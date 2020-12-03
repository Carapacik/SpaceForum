using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Identity;
using SpaceForum.Application.Email;
using SpaceForum.Infrastructure.Identity;

namespace SpaceForum.Web.Components.Account;

internal sealed class IdentityEmailSender(IEmailDelivery emailDelivery)
    : IEmailSender<ApplicationUser>
{
    public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink) =>
        emailDelivery.SendAsync(
            new(
                email,
                "Confirm your SpaceForum email",
                $"<p>Confirm your email to finish creating your SpaceForum account.</p><p><a href=\"{confirmationLink}\">Confirm email</a></p>"),
            CancellationToken.None);

    public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink) =>
        emailDelivery.SendAsync(
            new(
                email,
                "Reset your SpaceForum password",
                $"<p>A password reset was requested for your SpaceForum account.</p><p><a href=\"{resetLink}\">Reset password</a></p><p>If this wasn't you, you can ignore this email.</p>"),
            CancellationToken.None);

    public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode) =>
        emailDelivery.SendAsync(
            new(
                email,
                "Reset your SpaceForum password",
                $"<p>Your password reset code is <strong>{HtmlEncoder.Default.Encode(resetCode)}</strong>.</p>"),
            CancellationToken.None);
}
