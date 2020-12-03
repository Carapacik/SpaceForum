namespace SpaceForum.Application.Security;

public static class SecurityEventTypes
{
    public const string AccountRegistered = "identity.account.registered";
    public const string LoginSucceeded = "identity.login.succeeded";
    public const string LoginFailed = "identity.login.failed";
    public const string AccountLockedOut = "identity.account.locked-out";
    public const string EmailConfirmed = "identity.email.confirmed";
    public const string PasswordResetRequested = "identity.password-reset.requested";
    public const string PasswordResetCompleted = "identity.password-reset.completed";
    public const string AdministratorBootstrapped = "identity.administrator.bootstrapped";
    public const string TopicClosed = "discussion.topic.closed";
    public const string TopicReopened = "discussion.topic.reopened";
    public const string TopicDeleted = "discussion.topic.deleted";
    public const string PostDeleted = "discussion.post.deleted";
    public const string CategoryDeleted = "forum.category.deleted";
}

public sealed record SecurityAuditRecord(
    string EventType,
    Guid? ActorMemberId,
    string? SubjectId,
    bool Succeeded);

public interface ISecurityAuditWriter
{
    Task WriteAsync(SecurityAuditRecord record, CancellationToken cancellationToken);
}
