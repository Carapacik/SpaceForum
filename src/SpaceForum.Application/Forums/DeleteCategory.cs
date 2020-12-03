using SpaceForum.Application.Members;
using SpaceForum.Application.Security;

namespace SpaceForum.Application.Forums;

public enum DeleteCategoryStatus
{
    Deleted,
    NotFound,
    NotEmpty,
    Forbidden,
}

public sealed record DeleteCategoryResult(DeleteCategoryStatus Status, string? Error = null);

public sealed class DeleteCategoryHandler(
    IForumCategoryRepository categories,
    IMemberProfileRepository members,
    IForumAdministrationAccess administrationAccess,
    ISecurityAuditWriter audit)
{
    public async Task<DeleteCategoryResult> HandleAsync(
        Guid actorMemberId,
        Guid categoryId,
        CancellationToken cancellationToken)
    {
        if (!await administrationAccess.CanAdministerAsync(actorMemberId, cancellationToken))
        {
            return new(DeleteCategoryStatus.Forbidden);
        }

        var actor = await members.FindByIdAsync(actorMemberId, trackChanges: false, cancellationToken);
        if (!string.Equals(actor?.Login, "admin", StringComparison.Ordinal))
        {
            return new(
                DeleteCategoryStatus.Forbidden,
                "Only the root administrator with login 'admin' can delete categories.");
        }

        if (!await categories.ExistsAsync(categoryId, cancellationToken))
        {
            return new(DeleteCategoryStatus.NotFound, "The category was not found.");
        }

        if (!await categories.TryDeleteEmptyAsync(categoryId, cancellationToken))
        {
            return new(
                DeleteCategoryStatus.NotEmpty,
                "Move or delete child categories and topics before deleting this category.");
        }

        await audit.WriteAsync(
            new(SecurityEventTypes.CategoryDeleted, actorMemberId, categoryId.ToString(), Succeeded: true),
            cancellationToken);
        return new(DeleteCategoryStatus.Deleted);
    }
}
