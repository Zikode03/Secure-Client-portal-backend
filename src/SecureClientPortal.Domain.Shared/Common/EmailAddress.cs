namespace SecureClientPortal.Backend.Models;

public readonly record struct EmailAddress
{
    public string Value { get; }

    private EmailAddress(string value)
    {
        Value = value;
    }

    public static EmailAddress Parse(string value)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized) || !normalized.Contains('@'))
        {
            throw new DomainRuleException("A valid email address is required.");
        }

        return new EmailAddress(normalized);
    }

    public override string ToString() => Value;

    public static implicit operator string(EmailAddress email) => email.Value;
}
