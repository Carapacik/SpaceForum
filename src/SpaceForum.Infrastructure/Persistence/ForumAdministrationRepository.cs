using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using SpaceForum.Application.Forums;

namespace SpaceForum.Infrastructure.Persistence;

public sealed class ForumAdministrationRepository(NpgsqlDataSource dataSource) : IForumAdministrationRepository
{
    public async Task<ForumStatistics> GetStatisticsAsync(CancellationToken cancellationToken)
    {
        await using var command=dataSource.CreateCommand("SELECT (SELECT COUNT(*)::int FROM members.member_profiles),(SELECT COUNT(*)::int FROM discussions.topics WHERE \"HiddenAt\" IS NULL),(SELECT COUNT(*)::int FROM discussions.posts WHERE \"HiddenAt\" IS NULL),(SELECT COUNT(*)::int FROM discussions.post_reports WHERE \"Status\"='Open'),(SELECT COUNT(*)::int FROM discussions.notifications),(SELECT COUNT(*)::int FROM messaging.messages);");
        await using var reader=await command.ExecuteReaderAsync(cancellationToken);await reader.ReadAsync(cancellationToken);return new(reader.GetInt32(0),reader.GetInt32(1),reader.GetInt32(2),reader.GetInt32(3),reader.GetInt32(4),reader.GetInt32(5));
    }
    public async Task<IReadOnlyList<AuditEventView>> GetAuditAsync(int take,CancellationToken cancellationToken)
    {
        await using var command=dataSource.CreateCommand("SELECT event.\"Id\",event.\"OccurredAt\",event.\"EventType\",actor.\"Login\",event.\"SubjectId\",event.\"Succeeded\" FROM audit.security_audit_events AS event LEFT JOIN members.member_profiles AS actor ON actor.\"Id\"=event.\"ActorMemberId\" ORDER BY event.\"OccurredAt\" DESC LIMIT @take;");command.Parameters.AddWithValue("take",Math.Clamp(take,1,500));var items=new List<AuditEventView>();await using var reader=await command.ExecuteReaderAsync(cancellationToken);while(await reader.ReadAsync(cancellationToken))items.Add(new(reader.GetGuid(0),reader.GetFieldValue<DateTimeOffset>(1),reader.GetString(2),reader.IsDBNull(3)?null:reader.GetString(3),reader.IsDBNull(4)?null:reader.GetString(4),reader.GetBoolean(5)));return items;
    }
    public async Task<ForumSettingsView> GetSettingsAsync(CancellationToken cancellationToken)
    {
        var values=new Dictionary<string,string>();await using var command=dataSource.CreateCommand("SELECT \"Key\",\"Value\"#>>'{}' FROM configuration.forum_settings;");await using var reader=await command.ExecuteReaderAsync(cancellationToken);while(await reader.ReadAsync(cancellationToken))values[reader.GetString(0)]=reader.GetString(1);return new(values.GetValueOrDefault("forum.name","SpaceForum"),values.GetValueOrDefault("forum.description","A modern community forum."),values.GetValueOrDefault("forum.welcome","Good conversations deserve room to breathe."),values.GetValueOrDefault("appearance.accent","#6d28d9"),bool.TryParse(values.GetValueOrDefault("advanced.maintenance","false"),out var maintenance)&&maintenance);
    }
    public async Task SaveSettingsAsync(ForumSettingsView settings,Guid actorId,DateTimeOffset changedAt,CancellationToken cancellationToken)
    {
        var values=new Dictionary<string,string>{{"forum.name",settings.Name},{"forum.description",settings.Description},{"forum.welcome",settings.Welcome},{"appearance.accent",settings.AccentColor},{"advanced.maintenance",settings.MaintenanceMode.ToString().ToLowerInvariant()}};await using var connection=await dataSource.OpenConnectionAsync(cancellationToken);await using var transaction=await connection.BeginTransactionAsync(cancellationToken);foreach(var pair in values){await using var command=connection.CreateCommand();command.Transaction=transaction;command.CommandText="INSERT INTO configuration.forum_settings (\"Key\",\"Value\",\"UpdatedAt\",\"UpdatedByMemberId\") VALUES (@key,@value,@changedAt,@actorId) ON CONFLICT (\"Key\") DO UPDATE SET \"Value\"=EXCLUDED.\"Value\",\"UpdatedAt\"=EXCLUDED.\"UpdatedAt\",\"UpdatedByMemberId\"=EXCLUDED.\"UpdatedByMemberId\";";command.Parameters.AddWithValue("key",pair.Key);var parameter=command.Parameters.Add("value",NpgsqlDbType.Jsonb);parameter.Value=JsonSerializer.Serialize(pair.Value);command.Parameters.AddWithValue("changedAt",changedAt);command.Parameters.AddWithValue("actorId",actorId);await command.ExecuteNonQueryAsync(cancellationToken);}await transaction.CommitAsync(cancellationToken);
    }
}
