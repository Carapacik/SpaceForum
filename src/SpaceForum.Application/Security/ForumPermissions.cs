namespace SpaceForum.Application.Security;

public static class ForumPermissions
{
    public const string ClaimType = "spaceforum.permission";
    public const string ViewForum = "forum.view";
    public const string CreateTopic = "topic.create";
    public const string Reply = "topic.reply";
    public const string EditOwnPost = "post.edit-own";
    public const string ReportPost = "post.report";
    public const string ModeratePosts = "post.moderate";
    public const string ManageUsers = "user.manage";
    public static readonly string[] All = [ViewForum, CreateTopic, Reply, EditOwnPost, ReportPost, ModeratePosts, ManageUsers];
}
