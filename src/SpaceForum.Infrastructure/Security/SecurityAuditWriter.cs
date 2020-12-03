using Npgsql;
using SpaceForum.Application.Security;

namespace SpaceForum.Infrastructure.Security;

public sealed class SecurityAuditWriter(
    NpgsqlDataSource dataSource,
    TimeProvider timeProvider) : ISecurityAuditWriter
{
    public async Task WriteAsync(SecurityAuditRecord record, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(record.EventType);
        if (record.EventType.Length > 100 || record.SubjectId?.Length > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(record), "Security audit fields exceed their storage limit.");
        }

        await using var command = dataSource.CreateCommand(
            """
            INSERT INTO audit.security_audit_events
                ("Id", "OccurredAt", "EventType", "ActorMemberId", "SubjectId", "Succeeded")
            VALUES
                (@id, @occurredAt, @eventType, @actorMemberId, @subjectId, @succeeded);
            """);
        command.Parameters.AddWithValue("id", Guid.CreateVersion7());
        command.Parameters.AddWithValue("occurredAt", timeProvider.GetUtcNow());
        command.Parameters.AddWithValue("eventType", record.EventType);
        command.Parameters.AddWithValue("actorMemberId", (object?)record.ActorMemberId ?? DBNull.Value);
        command.Parameters.AddWithValue("subjectId", (object?)record.SubjectId ?? DBNull.Value);
        command.Parameters.AddWithValue("succeeded", record.Succeeded);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
