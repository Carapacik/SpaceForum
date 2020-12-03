using Microsoft.AspNetCore.Identity;
using SpaceForum.Application.Security;
using SpaceForum.Infrastructure.Identity;
using SpaceForum.Infrastructure.Persistence;

namespace SpaceForum.Web.Security;

internal sealed class AdminBootstrapper(
    SqlMigrationRunner migrationRunner,
    UserManager<ApplicationUser> userManager,
    ISecurityAuditWriter auditWriter,
    ILogger<AdminBootstrapper> logger)
{
    private static readonly Action<ILogger, Exception?> LogMissingEmail = LoggerMessage.Define(
        LogLevel.Error,
        new EventId(1, nameof(LogMissingEmail)),
        "BootstrapAdmin:Email must be provided through configuration.");

    private static readonly Action<ILogger, Exception?> LogPendingMigrations = LoggerMessage.Define(
        LogLevel.Error,
        new EventId(2, nameof(LogPendingMigrations)),
        "Database migrations are pending. Run the migrate service before bootstrapping an administrator.");

    private static readonly Action<ILogger, Exception?> LogAccountNotFound = LoggerMessage.Define(
        LogLevel.Error,
        new EventId(3, nameof(LogAccountNotFound)),
        "No registered account matches the configured bootstrap email.");

    private static readonly Action<ILogger, Exception?> LogEmailNotConfirmed = LoggerMessage.Define(
        LogLevel.Error,
        new EventId(4, nameof(LogEmailNotConfirmed)),
        "The target account must confirm its email before it can become an administrator.");

    private static readonly Action<ILogger, Exception?> LogAlreadyAdministrator = LoggerMessage.Define(
        LogLevel.Information,
        new EventId(5, nameof(LogAlreadyAdministrator)),
        "The target account is already an administrator; no change was made.");

    private static readonly Action<ILogger, string, Exception?> LogAssignmentFailed = LoggerMessage.Define<string>(
        LogLevel.Error,
        new EventId(6, nameof(LogAssignmentFailed)),
        "Administrator assignment failed with Identity error codes: {ErrorCodes}");

    private static readonly Action<ILogger, Guid, Exception?> LogAssignmentSucceeded = LoggerMessage.Define<Guid>(
        LogLevel.Information,
        new EventId(7, nameof(LogAssignmentSucceeded)),
        "Administrator role assigned successfully to user {UserId}.");

    public async Task<int> RunAsync(string? email, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            LogMissingEmail(logger, null);
            return 2;
        }

        if (await migrationRunner.HasPendingAsync(cancellationToken))
        {
            LogPendingMigrations(logger, null);
            return 3;
        }

        var user = await userManager.FindByEmailAsync(email.Trim());
        if (user is null)
        {
            LogAccountNotFound(logger, null);
            await auditWriter.WriteAsync(
                new(SecurityEventTypes.AdministratorBootstrapped, null, null, Succeeded: false),
                cancellationToken);
            return 4;
        }

        if (!await userManager.IsEmailConfirmedAsync(user))
        {
            LogEmailNotConfirmed(logger, null);
            await auditWriter.WriteAsync(
                new(SecurityEventTypes.AdministratorBootstrapped, null, user.Id.ToString(), Succeeded: false),
                cancellationToken);
            return 5;
        }

        if (await userManager.IsInRoleAsync(user, ForumRoles.Administrator))
        {
            LogAlreadyAdministrator(logger, null);
            return 0;
        }

        var result = await userManager.AddToRoleAsync(user, ForumRoles.Administrator);
        if (!result.Succeeded)
        {
            LogAssignmentFailed(logger, string.Join(", ", result.Errors.Select(error => error.Code)), null);
            await auditWriter.WriteAsync(
                new(SecurityEventTypes.AdministratorBootstrapped, null, user.Id.ToString(), Succeeded: false),
                cancellationToken);
            return 6;
        }

        await auditWriter.WriteAsync(
            new(SecurityEventTypes.AdministratorBootstrapped, user.Id, user.Id.ToString(), Succeeded: true),
            cancellationToken);
        LogAssignmentSucceeded(logger, user.Id, null);
        return 0;
    }
}
