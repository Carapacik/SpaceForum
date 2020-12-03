using Npgsql;
using SpaceForum.Application.Members;
using SpaceForum.Domain.Members;

namespace SpaceForum.Infrastructure.Persistence;

public sealed class MemberProfileRepository(NpgsqlDataSource dataSource) : IMemberProfileRepository
{
    private MemberProfile? trackedMember;

    public async Task<bool> TryAddAsync(MemberProfile member, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            INSERT INTO members.member_profiles
                ("Id", "Login", "DisplayName", "Biography", "Location", "Website", "CreatedAt", "UpdatedAt", "Version")
            VALUES
                (@id, @login, @displayName, @biography, @location, @website, @createdAt, @updatedAt, @version);
            """);
        AddMemberParameters(command, member);
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
            return true;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return false;
        }
    }

    public async Task<bool> LoginExistsAsync(string normalizedLogin, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "SELECT EXISTS (SELECT 1 FROM members.member_profiles WHERE \"Login\" = @login);"
        );
        command.Parameters.AddWithValue("login", normalizedLogin);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    public async Task<MemberProfile?> FindByIdAsync(
        Guid id,
        bool trackChanges,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT "Id", "Login", "DisplayName", "Biography", "Location", "Website", "CreatedAt", "UpdatedAt", "Version"
            FROM members.member_profiles
            WHERE "Id" = @id;
            """);
        command.Parameters.AddWithValue("id", id);
        var member = await ReadSingleAsync(command, cancellationToken);
        trackedMember = trackChanges ? member : null;
        return member;
    }

    public async Task<MemberProfile?> FindByLoginAsync(
        string normalizedLogin,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT "Id", "Login", "DisplayName", "Biography", "Location", "Website", "CreatedAt", "UpdatedAt", "Version"
            FROM members.member_profiles
            WHERE "Login" = @login;
            """);
        command.Parameters.AddWithValue("login", normalizedLogin);
        return await ReadSingleAsync(command, cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        var member = trackedMember
            ?? throw new InvalidOperationException("A member must be loaded for update before it can be saved.");
        await using var command = dataSource.CreateCommand(
            """
            UPDATE members.member_profiles
            SET "DisplayName" = @displayName,
                "Biography" = @biography,
                "Location" = @location,
                "Website" = @website,
                "UpdatedAt" = @updatedAt,
                "Version" = @version
            WHERE "Id" = @id AND "Version" = @expectedVersion;
            """);
        AddMemberParameters(command, member);
        command.Parameters.AddWithValue("expectedVersion", member.Version - 1);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("The member profile was changed by another request.");
        }

        trackedMember = null;
    }

    private static async Task<MemberProfile?> ReadSingleAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadMember(reader) : null;
    }

    private static MemberProfile ReadMember(NpgsqlDataReader reader) => MemberProfile.Restore(
        reader.GetGuid(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.IsDBNull(3) ? null : reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetString(4),
        reader.IsDBNull(5) ? null : reader.GetString(5),
        reader.GetFieldValue<DateTimeOffset>(6),
        reader.GetFieldValue<DateTimeOffset>(7),
        reader.GetInt64(8));

    private static void AddMemberParameters(NpgsqlCommand command, MemberProfile member)
    {
        command.Parameters.AddWithValue("id", member.Id);
        command.Parameters.AddWithValue("login", member.Login);
        command.Parameters.AddWithValue("displayName", member.DisplayName);
        command.Parameters.AddWithValue("biography", (object?)member.Biography ?? DBNull.Value);
        command.Parameters.AddWithValue("location", (object?)member.Location ?? DBNull.Value);
        command.Parameters.AddWithValue("website", (object?)member.Website ?? DBNull.Value);
        command.Parameters.AddWithValue("createdAt", member.CreatedAt);
        command.Parameters.AddWithValue("updatedAt", member.UpdatedAt);
        command.Parameters.AddWithValue("version", member.Version);
    }
}
