using Microsoft.Extensions.Caching.Memory;
using SpaceForum.Application.Forums;

namespace SpaceForum.Web.Services;

public sealed class ForumSettingsService(
    IForumAdministrationRepository repository,
    IMemoryCache cache)
{
    private const string CacheKey = "forum-settings";

    public async Task<ForumSettingsView> GetAsync(CancellationToken cancellationToken)
    {
        if (cache.TryGetValue<ForumSettingsView>(CacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        var settings = await repository.GetSettingsAsync(cancellationToken);
        cache.Set(CacheKey, settings, TimeSpan.FromMinutes(5));
        return settings;
    }

    public async Task SaveAsync(
        ForumSettingsView settings,
        Guid actorId,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken)
    {
        await repository.SaveSettingsAsync(settings, actorId, changedAt, cancellationToken);
        cache.Remove(CacheKey);
    }
}
