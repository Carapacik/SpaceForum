using SpaceForum.Domain;
using SpaceForum.Domain.Members;

namespace SpaceForum.Application.Members;

public sealed class GetMemberProfileHandler(IMemberProfileRepository repository)
{
    public async Task<MemberProfileView?> ByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var member = await repository.FindByIdAsync(id, trackChanges: false, cancellationToken);
        return member is null ? null : Map(member);
    }

    public async Task<MemberProfileView?> ByLoginAsync(string login, CancellationToken cancellationToken)
    {
        string normalizedLogin;
        try
        {
            normalizedLogin = MemberProfile.NormalizeLogin(login);
        }
        catch (DomainRuleViolationException)
        {
            return null;
        }

        var member = await repository.FindByLoginAsync(normalizedLogin, cancellationToken);
        return member is null ? null : Map(member);
    }

    private static MemberProfileView Map(MemberProfile member) =>
        new(
            member.Id,
            member.Login,
            member.DisplayName,
            member.Biography,
            member.Location,
            member.Website,
            member.CreatedAt);
}
