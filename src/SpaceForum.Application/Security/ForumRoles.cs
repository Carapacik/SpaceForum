namespace SpaceForum.Application.Security;

public static class ForumRoles
{
    public const string Member = "Member";
    public const string Moderator = "Moderator";
    public const string Administrator = "Administrator";

    public static IReadOnlyList<string> All { get; } = [Member, Moderator, Administrator];
}
