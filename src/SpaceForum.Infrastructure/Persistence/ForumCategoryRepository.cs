using Npgsql;
using SpaceForum.Application.Forums;
using SpaceForum.Domain.Forums;

namespace SpaceForum.Infrastructure.Persistence;

public sealed class ForumCategoryRepository(NpgsqlDataSource dataSource) : IForumCategoryRepository
{
    private const string SelectColumns =
        "\"Id\", \"Name\", \"Slug\", \"Description\", \"Format\", \"ParentId\", \"Position\", \"CreatedAt\", \"UpdatedAt\", \"Version\"";

    public async Task<IReadOnlyList<ForumCategory>> ListAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"SELECT {SelectColumns} FROM forums.categories ORDER BY \"ParentId\" IS NOT NULL, \"Position\", \"Name\";"
        );
        var categories = new List<ForumCategory>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            categories.Add(ReadCategory(reader));
        }

        return categories;
    }

    public async Task<ForumCategory?> FindBySlugAsync(string normalizedSlug, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"SELECT {SelectColumns} FROM forums.categories WHERE \"Slug\" = @slug;"
        );
        command.Parameters.AddWithValue("slug", normalizedSlug);
        return await ReadSingleAsync(command, cancellationToken);
    }

    public async Task<ForumCategory?> FindByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"SELECT {SelectColumns} FROM forums.categories WHERE \"Id\" = @id;"
        );
        command.Parameters.AddWithValue("id", id);
        return await ReadSingleAsync(command, cancellationToken);
    }

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken) =>
        ExistsAsync("\"Id\"", id, cancellationToken);

    public Task<bool> SlugExistsAsync(string normalizedSlug, CancellationToken cancellationToken) =>
        ExistsAsync("\"Slug\"", normalizedSlug, cancellationToken);

    public async Task<bool> TryAddAsync(ForumCategory category, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            INSERT INTO forums.categories
                ("Id", "Name", "Slug", "Description", "Format", "ParentId", "Position", "CreatedAt", "UpdatedAt", "Version")
            VALUES
                (@id, @name, @slug, @description, @format, @parentId, @position, @createdAt, @updatedAt, @version);
            """);
        command.Parameters.AddWithValue("id", category.Id);
        command.Parameters.AddWithValue("name", category.Name);
        command.Parameters.AddWithValue("slug", category.Slug);
        command.Parameters.AddWithValue("description", category.Description);
        command.Parameters.AddWithValue("format", ToDatabaseFormat(category.Format));
        command.Parameters.AddWithValue("parentId", (object?)category.ParentId ?? DBNull.Value);
        command.Parameters.AddWithValue("position", category.Position);
        command.Parameters.AddWithValue("createdAt", category.CreatedAt);
        command.Parameters.AddWithValue("updatedAt", category.UpdatedAt);
        command.Parameters.AddWithValue("version", category.Version);
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

    public async Task<bool> TryDeleteEmptyAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            DELETE FROM forums.categories AS category
            WHERE category."Id" = @id
              AND NOT EXISTS (
                  SELECT 1 FROM forums.categories AS child WHERE child."ParentId" = category."Id")
              AND NOT EXISTS (
                  SELECT 1 FROM discussions.topics AS topic WHERE topic."CategoryId" = category."Id");
            """);
        command.Parameters.AddWithValue("id", id);
        try
        {
            return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            return false;
        }
    }

    private async Task<bool> ExistsAsync<T>(string column, T value, CancellationToken cancellationToken)
        where T : notnull
    {
        await using var command = dataSource.CreateCommand(
            $"SELECT EXISTS (SELECT 1 FROM forums.categories WHERE {column} = @value);"
        );
        command.Parameters.AddWithValue("value", value);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static async Task<ForumCategory?> ReadSingleAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadCategory(reader) : null;
    }

    private static ForumCategory ReadCategory(NpgsqlDataReader reader) => ForumCategory.Restore(
        reader.GetGuid(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        FromDatabaseFormat(reader.GetString(4)),
        reader.IsDBNull(5) ? null : reader.GetGuid(5),
        reader.GetInt32(6),
        reader.GetFieldValue<DateTimeOffset>(7),
        reader.GetFieldValue<DateTimeOffset>(8),
        reader.GetInt64(9));

    private static string ToDatabaseFormat(CategoryFormat format) => format switch
    {
        CategoryFormat.Discussion => "Discussion",
        CategoryFormat.QuestionAndAnswer => "Q&A",
        CategoryFormat.Announcement => "Announcement",
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    private static CategoryFormat FromDatabaseFormat(string format) => format switch
    {
        "Discussion" => CategoryFormat.Discussion,
        "Q&A" or "QuestionAndAnswer" => CategoryFormat.QuestionAndAnswer,
        "Announcement" => CategoryFormat.Announcement,
        _ => throw new InvalidOperationException($"Unknown persisted category format '{format}'."),
    };
}
