namespace SpaceForum.Application.Forums;

public sealed record ForumStatistics(int Members, int Topics, int Posts, int OpenReports, int Notifications, int Messages);
public sealed record AuditEventView(Guid Id, DateTimeOffset OccurredAt, string EventType, string? ActorLogin, string? SubjectId, bool Succeeded);
public sealed record ForumSettingsView(string Name, string Description, string Welcome, string AccentColor, bool MaintenanceMode);

public interface IForumAdministrationRepository
{
    Task<ForumStatistics> GetStatisticsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<AuditEventView>> GetAuditAsync(int take, CancellationToken cancellationToken);
    Task<ForumSettingsView> GetSettingsAsync(CancellationToken cancellationToken);
    Task SaveSettingsAsync(ForumSettingsView settings, Guid actorId, DateTimeOffset changedAt, CancellationToken cancellationToken);
}
