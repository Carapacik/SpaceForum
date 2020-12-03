namespace SpaceForum.Application.Email;

public interface IEmailDelivery
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}
