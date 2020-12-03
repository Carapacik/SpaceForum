using SpaceForum.Domain;
using SpaceForum.Domain.Members;

namespace SpaceForum.Application.Members;

public sealed record CreateMemberProfileCommand(Guid MemberId, string Login, string DisplayName);

public enum CreateMemberProfileStatus
{
    Created,
    Invalid,
    LoginUnavailable,
}

public sealed record CreateMemberProfileResult(CreateMemberProfileStatus Status, string? Error = null);

public sealed class CreateMemberProfileHandler(
    IMemberProfileRepository repository,
    TimeProvider timeProvider)
{
    public async Task<CreateMemberProfileResult> HandleAsync(
        CreateMemberProfileCommand command,
        CancellationToken cancellationToken)
    {
        string normalizedLogin;
        try
        {
            normalizedLogin = MemberProfile.NormalizeLogin(command.Login);
        }
        catch (DomainRuleViolationException exception)
        {
            return new(CreateMemberProfileStatus.Invalid, exception.Message);
        }

        if (await repository.LoginExistsAsync(normalizedLogin, cancellationToken))
        {
            return new(CreateMemberProfileStatus.LoginUnavailable, "This login is already in use.");
        }

        try
        {
            var member = MemberProfile.Create(
                command.MemberId,
                normalizedLogin,
                command.DisplayName,
                timeProvider.GetUtcNow());
            if (!await repository.TryAddAsync(member, cancellationToken))
            {
                return new(CreateMemberProfileStatus.LoginUnavailable, "This login is already in use.");
            }

            return new(CreateMemberProfileStatus.Created);
        }
        catch (DomainRuleViolationException exception)
        {
            return new(CreateMemberProfileStatus.Invalid, exception.Message);
        }
    }
}
