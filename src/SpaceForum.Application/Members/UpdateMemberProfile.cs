using SpaceForum.Domain;
using SpaceForum.Domain.Members;

namespace SpaceForum.Application.Members;

public sealed record UpdateMemberProfileCommand(
    Guid ActorMemberId,
    Guid ProfileMemberId,
    string DisplayName,
    string? Biography,
    string? Location,
    string? Website);

public enum UpdateMemberProfileStatus
{
    Updated,
    Invalid,
    Forbidden,
    NotFound,
}

public sealed record UpdateMemberProfileResult(UpdateMemberProfileStatus Status, string? Error = null);

public sealed class UpdateMemberProfileHandler(
    IMemberProfileRepository repository,
    TimeProvider timeProvider)
{
    public async Task<UpdateMemberProfileResult> HandleAsync(
        UpdateMemberProfileCommand command,
        CancellationToken cancellationToken)
    {
        if (command.ActorMemberId != command.ProfileMemberId)
        {
            return new(UpdateMemberProfileStatus.Forbidden);
        }

        var member = await repository.FindByIdAsync(
            command.ProfileMemberId,
            trackChanges: true,
            cancellationToken);
        if (member is null)
        {
            return new(UpdateMemberProfileStatus.NotFound);
        }

        try
        {
            member.Update(
                command.DisplayName,
                command.Biography,
                command.Location,
                command.Website,
                timeProvider.GetUtcNow());
            await repository.SaveChangesAsync(cancellationToken);
            return new(UpdateMemberProfileStatus.Updated);
        }
        catch (DomainRuleViolationException exception)
        {
            return new(UpdateMemberProfileStatus.Invalid, exception.Message);
        }
    }
}
