using System.Text.RegularExpressions;

namespace BrokerageMonitor.Domain.ValueObjects;

public sealed class EmailAddress
{
    private static readonly Regex EmailRegex = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(100));

    public string Value { get; }

    public EmailAddress(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Email address cannot be empty.", nameof(value));

        if (!EmailRegex.IsMatch(value))
            throw new ArgumentException($"'{value}' is not a valid email address.", nameof(value));

        Value = value.Trim().ToLowerInvariant();
    }

    public override bool Equals(object? obj) =>
        obj is EmailAddress other && Value == other.Value;

    public override int GetHashCode() => Value.GetHashCode();

    public override string ToString() => Value;
}
