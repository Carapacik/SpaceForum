namespace SpaceForum.Application.Members;

public sealed record MemberProfileView(
    Guid Id,
    string Login,
    string DisplayName,
    string? Biography,
    string? Location,
    string? Website,
    DateTimeOffset CreatedAt);
