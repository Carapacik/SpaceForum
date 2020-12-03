using SpaceForum.Domain;
using SpaceForum.Domain.Forums;

namespace SpaceForum.Application.Forums;

public sealed record CreateCategoryCommand(
    Guid ActorMemberId,
    string Name,
    string Slug,
    string Description,
    CategoryFormat Format,
    Guid? ParentId,
    int Position);

public enum CreateCategoryStatus
{
    Created,
    Invalid,
    Forbidden,
    ParentNotFound,
    SlugUnavailable,
}

public sealed record CreateCategoryResult(CreateCategoryStatus Status, string? Error = null);

public sealed class CreateCategoryHandler(
    IForumCategoryRepository repository,
    IForumAdministrationAccess administrationAccess,
    TimeProvider timeProvider)
{
    public async Task<CreateCategoryResult> HandleAsync(
        CreateCategoryCommand command,
        CancellationToken cancellationToken)
    {
        if (!await administrationAccess.CanAdministerAsync(command.ActorMemberId, cancellationToken))
        {
            return new(CreateCategoryStatus.Forbidden);
        }

        string normalizedSlug;
        try
        {
            normalizedSlug = ForumCategory.NormalizeSlug(command.Slug);
        }
        catch (DomainRuleViolationException exception)
        {
            return new(CreateCategoryStatus.Invalid, exception.Message);
        }

        if (command.ParentId is Guid parentId && !await repository.ExistsAsync(parentId, cancellationToken))
        {
            return new(CreateCategoryStatus.ParentNotFound, "The parent category does not exist.");
        }

        if (await repository.SlugExistsAsync(normalizedSlug, cancellationToken))
        {
            return new(CreateCategoryStatus.SlugUnavailable, "This category slug is already in use.");
        }

        try
        {
            var category = ForumCategory.Create(
                Guid.CreateVersion7(),
                command.Name,
                normalizedSlug,
                command.Description,
                command.Format,
                command.ParentId,
                command.Position,
                timeProvider.GetUtcNow());
            if (!await repository.TryAddAsync(category, cancellationToken))
            {
                return new(CreateCategoryStatus.SlugUnavailable, "This category slug is already in use.");
            }

            return new(CreateCategoryStatus.Created);
        }
        catch (DomainRuleViolationException exception)
        {
            return new(CreateCategoryStatus.Invalid, exception.Message);
        }
    }
}
