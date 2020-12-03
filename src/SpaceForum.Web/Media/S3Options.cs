namespace SpaceForum.Web.Media;

public sealed class S3Options
{
    public const string SectionName = "S3";

    public string ServiceUrl { get; set; } = string.Empty;

    public string AccessKey { get; set; } = string.Empty;

    public string SecretKey { get; set; } = string.Empty;

    public string BucketName { get; set; } = "spaceforum-media";
}
