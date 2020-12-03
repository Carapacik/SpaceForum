using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;
using SpaceForum.Application.Email;

namespace SpaceForum.Infrastructure.Email;

public sealed class SmtpEmailDelivery(IOptions<SmtpOptions> options) : IEmailDelivery
{
    private readonly SmtpOptions options = options.Value;

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        using var mailMessage = new MailMessage
        {
            From = new MailAddress(options.FromAddress, options.FromName),
            Subject = message.Subject,
            Body = message.HtmlBody,
            IsBodyHtml = true,
        };
        mailMessage.To.Add(new MailAddress(message.Recipient));

        using var client = new SmtpClient(options.Host, options.Port)
        {
            EnableSsl = options.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
        };

        if (!string.IsNullOrWhiteSpace(options.Username))
        {
            client.Credentials = new NetworkCredential(options.Username, options.Password);
        }

        await client.SendMailAsync(mailMessage, cancellationToken);
    }
}
