using SpaceForum.Domain;
using SpaceForum.Domain.Forums;

namespace SpaceForum.Application.Forums;

public sealed record CategoryView(
    Guid Id,
    string Name,
    string Slug,
    string Description,
    CategoryFormat Format,
    Guid? ParentId,
    int Position);

public sealed class GetCategoriesHandler(IForumCategoryRepository repository)
{
    public async Task<IReadOnlyList<CategoryView>> ListAsync(CancellationToken cancellationToken) =>
        (await repository.ListAsync(cancellationToken)).Select(Map).ToArray();

    public async Task<CategoryView?> BySlugAsync(string slug, CancellationToken cancellationToken)
    {
        string normalizedSlug;
        try
        {
            normalizedSlug = ForumCategory.NormalizeSlug(slug);
        }
        catch (DomainRuleViolationException)
        {
            return null;
        }

        var category = await repository.FindBySlugAsync(normalizedSlug, cancellationToken);
        return category is null ? null : Map(category);
    }

    private static CategoryView Map(ForumCategory category) =>
        new(
            category.Id,
            category.Name,
            category.Slug,
            category.Description,
            category.Format,
            category.ParentId,
            category.Position);
}
