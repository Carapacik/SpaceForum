using Microsoft.EntityFrameworkCore;
using SpaceForum.Infrastructure.Identity;
using SpaceForum.Infrastructure.Persistence;

namespace SpaceForum.IntegrationTests.Persistence;

public sealed class SqlPersistenceContractTests
{
    [Fact]
    public void IdentityDbContextDoesNotMapForumEntities()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=spaceforum_contract_test")
            .Options;
        using var dbContext = new ApplicationDbContext(options);

        Assert.NotNull(dbContext.Model.FindEntityType(typeof(ApplicationUser)));
        Assert.DoesNotContain(
            dbContext.Model.GetEntityTypes(),
            entity => entity.ClrType.Namespace?.StartsWith("SpaceForum.Domain", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task InitialSqlMigrationDefinesApplicationConstraints()
    {
        var assembly = typeof(SqlMigrationRunner).Assembly;
        var resource = Assert.Single(
            assembly.GetManifestResourceNames(),
            name => name.EndsWith("0001_initial.sql", StringComparison.Ordinal));
        await using var stream = assembly.GetManifestResourceStream(resource);
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        var sql = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

        Assert.Contains("CREATE UNIQUE INDEX IF NOT EXISTS \"IX_member_profiles_Login\"", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS discussions.topic_votes", sql, StringComparison.Ordinal);
        Assert.Contains("IX_topics_Title_Search", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE UNIQUE INDEX \"EmailIndex\"", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS audit.security_audit_events", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS discussions.post_revisions", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS discussions.notifications", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS discussions.post_reports", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS discussions.post_reactions", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS discussions.post_bookmarks", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS discussions.attachments", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Content\" bytea", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS messaging.conversations", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS configuration.forum_settings", sql, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "FOREIGN KEY (\"ActorMemberId\")",
            sql,
            StringComparison.Ordinal);
    }
}
