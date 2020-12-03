using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace SpaceForum.Infrastructure.Persistence;

public sealed class SqlMigrationRunner(NpgsqlDataSource dataSource)
{
    private const string ResourcePrefix = "SpaceForum.Infrastructure.Persistence.SqlMigrations.";

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await ExecuteAsync(
            connection,
            transaction,
            """
            SELECT pg_advisory_xact_lock(hashtext('spaceforum-schema-migrations'));
            CREATE SCHEMA IF NOT EXISTS infrastructure;
            CREATE TABLE IF NOT EXISTS infrastructure.schema_migrations (
                name varchar(200) PRIMARY KEY,
                checksum char(64) NOT NULL,
                applied_at timestamptz NOT NULL
            );
            """,
            cancellationToken);

        var applied = await ReadAppliedAsync(connection, transaction, cancellationToken);
        var assembly = typeof(SqlMigrationRunner).Assembly;
        var resources = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal) && name.EndsWith(".sql", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        foreach (var resourceName in resources)
        {
            await using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded migration '{resourceName}' was not found.");
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var sql = await reader.ReadToEndAsync(cancellationToken);
            var migrationName = resourceName[ResourcePrefix.Length..^4];
            var checksum = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sql)));

            if (applied.TryGetValue(migrationName, out var existingChecksum))
            {
                if (!string.Equals(existingChecksum, checksum, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Applied SQL migration '{migrationName}' has been modified.");
                }

                continue;
            }

            await ExecuteAsync(connection, transaction, sql, cancellationToken);
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText =
                """
                INSERT INTO infrastructure.schema_migrations (name, checksum, applied_at)
                VALUES (@name, @checksum, @appliedAt);
                """;
            insert.Parameters.AddWithValue("name", migrationName);
            insert.Parameters.AddWithValue("checksum", checksum);
            insert.Parameters.AddWithValue("appliedAt", DateTimeOffset.UtcNow);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<bool> HasPendingAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.tables
                WHERE table_schema = 'infrastructure' AND table_name = 'schema_migrations');
            """;
        var historyExists = (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
        if (!historyExists)
        {
            return true;
        }

        var applied = await ReadAppliedAsync(connection, transaction: null, cancellationToken);
        var resources = typeof(SqlMigrationRunner).Assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal) && name.EndsWith(".sql", StringComparison.Ordinal));
        return resources.Any(resource => !applied.ContainsKey(resource[ResourcePrefix.Length..^4]));
    }

    private static async Task<Dictionary<string, string>> ReadAppliedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT name, checksum FROM infrastructure.schema_migrations;";
        var migrations = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            migrations.Add(reader.GetString(0), reader.GetString(1));
        }

        return migrations;
    }

    private static async Task ExecuteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
