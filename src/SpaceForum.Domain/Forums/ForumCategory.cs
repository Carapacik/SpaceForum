using SpaceForum.Domain;
using SpaceForum.Domain.Members;

namespace SpaceForum.Domain.Forums;

public sealed class ForumCategory
{
    public const int NameMaxLength = 60;
    public const int SlugMaxLength = 60;
    public const int DescriptionMaxLength = 240;

    private ForumCategory()
    {
    }

    private ForumCategory(
        Guid id,
        string name,
        string slug,
        string description,
        CategoryFormat format,
        Guid? parentId,
        int position,
        DateTimeOffset createdAt)
    {
        Id = id;
        Name = name;
        Slug = slug;
        Description = description;
        Format = format;
        ParentId = parentId;
        Position = position;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
        Version = 1;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Slug { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public CategoryFormat Format { get; private set; }

    public Guid? ParentId { get; private set; }

    public int Position { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public long Version { get; private set; }

    public static ForumCategory Create(
        Guid id,
        string name,
        string slug,
        string description,
        CategoryFormat format,
        Guid? parentId,
        int position,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new DomainRuleViolationException("A category ID is required.");
        }

        if (parentId == id)
        {
            throw new DomainRuleViolationException("A category cannot be its own parent.");
        }

        if (!Enum.IsDefined(format))
        {
            throw new DomainRuleViolationException("The category format is invalid.");
        }

        if (position < 0)
        {
            throw new DomainRuleViolationException("Category position cannot be negative.");
        }

        return new(
            id,
            NormalizeRequiredText(name, NameMaxLength, "Category name"),
            NormalizeSlug(slug),
            NormalizeRequiredText(description, DescriptionMaxLength, "Category description"),
            format,
            parentId,
            position,
            createdAt.ToUniversalTime());
    }

    internal static ForumCategory Restore(
        Guid id,
        string name,
        string slug,
        string description,
        CategoryFormat format,
        Guid? parentId,
        int position,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        long version)
    {
        var category = Create(id, name, slug, description, format, parentId, position, createdAt);
        var utcUpdatedAt = updatedAt.ToUniversalTime();
        if (utcUpdatedAt < category.CreatedAt || version < 1)
        {
            throw new DomainRuleViolationException("Persisted category state is invalid.");
        }

        category.UpdatedAt = utcUpdatedAt;
        category.Version = version;
        return category;
    }

    public static string NormalizeSlug(string slug)
    {
        var normalized = (slug ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length is < 2 or > SlugMaxLength)
        {
            throw new DomainRuleViolationException($"Category slug must contain between 2 and {SlugMaxLength} characters.");
        }

        if (!IsAsciiLetterOrDigit(normalized[0])
            || !IsAsciiLetterOrDigit(normalized[^1])
            || normalized.Any(character => !IsAsciiLetterOrDigit(character) && character != '-'))
        {
            throw new DomainRuleViolationException(
                "Category slug must start and end with a letter or digit and contain only ASCII letters, digits, or hyphens.");
        }

        return normalized;
    }

    private static string NormalizeRequiredText(string value, int maxLength, string fieldName)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length is < 2 || normalized.Length > maxLength)
        {
            throw new DomainRuleViolationException($"{fieldName} must contain between 2 and {maxLength} characters.");
        }

        return normalized;
    }

    private static bool IsAsciiLetterOrDigit(char character) =>
        character is >= 'a' and <= 'z' or >= '0' and <= '9';
}
