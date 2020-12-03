namespace SpaceForum.Domain;

public sealed class DomainRuleViolationException(string message) : Exception(message);
