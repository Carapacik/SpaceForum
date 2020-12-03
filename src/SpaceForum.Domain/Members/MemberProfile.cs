using SpaceForum.Domain;

namespace SpaceForum.Domain.Members;

public sealed class MemberProfile
{
    public const int LoginMaxLength = 30;
    public const int DisplayNameMaxLength = 50;
    public const int BiographyMaxLength = 500;
    public const int LocationMaxLength = 100;
    public const int WebsiteMaxLength = 200;

    private MemberProfile()
    {
    }

    private MemberProfile(Guid id, string login, string displayName, DateTimeOffset createdAt)
    {
        Id = id;
        Login = login;
        DisplayName = displayName;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
        Version = 1;
    }

    public Guid Id { get; private set; }

    public string Login { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public string? Biography { get; private set; }

    public string? Location { get; private set; }

    public string? Website { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public long Version { get; private set; }

    public static MemberProfile Create(
        Guid id,
        string login,
        string displayName,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new DomainRuleViolationException("A member ID is required.");
        }

        return new MemberProfile(
            id,
            NormalizeLogin(login),
            ValidateDisplayName(displayName),
            createdAt.ToUniversalTime());
    }

    internal static MemberProfile Restore(
        Guid id,
        string login,
        string displayName,
        string? biography,
        string? location,
        string? website,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        long version)
    {
        var member = Create(id, login, displayName, createdAt);
        var utcUpdatedAt = updatedAt.ToUniversalTime();
        if (utcUpdatedAt < member.CreatedAt || version < 1)
        {
            throw new DomainRuleViolationException("Persisted member state is invalid.");
        }

        member.Biography = NormalizeOptionalText(biography, BiographyMaxLength, "Biography");
        member.Location = NormalizeOptionalText(location, LocationMaxLength, "Location");
        member.Website = NormalizeWebsite(website);
        member.UpdatedAt = utcUpdatedAt;
        member.Version = version;
        return member;
    }

    public void Update(
        string displayName,
        string? biography,
        string? location,
        string? website,
        DateTimeOffset updatedAt)
    {
        var utcUpdatedAt = updatedAt.ToUniversalTime();
        if (utcUpdatedAt < UpdatedAt)
        {
            throw new DomainRuleViolationException("The update time cannot be earlier than the previous update.");
        }

        DisplayName = ValidateDisplayName(displayName);
        Biography = NormalizeOptionalText(biography, BiographyMaxLength, "Biography");
        Location = NormalizeOptionalText(location, LocationMaxLength, "Location");
        Website = NormalizeWebsite(website);
        UpdatedAt = utcUpdatedAt;
        Version++;
    }

    public static string NormalizeLogin(string login)
    {
        var normalized = (login ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length is < 3 or > LoginMaxLength)
        {
            throw new DomainRuleViolationException($"Login must contain between 3 and {LoginMaxLength} characters.");
        }

        if (normalized.Any(character => !IsAsciiLetterOrDigit(character)))
        {
            throw new DomainRuleViolationException(
                "Login can contain only lowercase ASCII letters and digits.");
        }

        return normalized;
    }

    private static string ValidateDisplayName(string displayName)
    {
        var normalized = (displayName ?? string.Empty).Trim();
        if (normalized.Length is < 2 or > DisplayNameMaxLength)
        {
            throw new DomainRuleViolationException(
                $"Display name must contain between 2 and {DisplayNameMaxLength} characters.");
        }

        return normalized;
    }

    private static string? NormalizeOptionalText(string? value, int maxLength, string fieldName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return null;
        }

        if (normalized.Length > maxLength)
        {
            throw new DomainRuleViolationException($"{fieldName} cannot exceed {maxLength} characters.");
        }

        return normalized;
    }

    private static string? NormalizeWebsite(string? website)
    {
        var normalized = NormalizeOptionalText(website, WebsiteMaxLength, "Website");
        if (normalized is null)
        {
            return null;
        }

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            throw new DomainRuleViolationException("Website must be an absolute HTTP or HTTPS URL.");
        }

        return uri.AbsoluteUri;
    }

    private static bool IsAsciiLetterOrDigit(char character) =>
        character is >= 'a' and <= 'z' or >= '0' and <= '9';
}
