namespace BrokerageMonitor.Domain.ValueObjects;

/// <summary>
/// Mail parsing rule for a MonitoredComponent.
/// BI-016: SuccessKeywords and FailureKeywords must not overlap.
/// BI-016: On conflict, FailureKeywords take priority.
/// </summary>
public sealed class MailParsingRule
{
    public string FromPattern { get; }
    public string SubjectPattern { get; }
    public IReadOnlyList<string> SuccessKeywords { get; }
    public IReadOnlyList<string> FailureKeywords { get; }

    public MailParsingRule(
        string fromPattern,
        string subjectPattern,
        IReadOnlyList<string> successKeywords,
        IReadOnlyList<string> failureKeywords)
    {
        if (string.IsNullOrWhiteSpace(fromPattern))
            throw new ArgumentException("FromPattern cannot be empty.", nameof(fromPattern));

        if (string.IsNullOrWhiteSpace(subjectPattern))
            throw new ArgumentException("SubjectPattern cannot be empty.", nameof(subjectPattern));

        ArgumentNullException.ThrowIfNull(successKeywords);
        ArgumentNullException.ThrowIfNull(failureKeywords);

        var overlapping = successKeywords
            .Intersect(failureKeywords, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (overlapping.Count > 0)
            throw new ArgumentException(
                $"SuccessKeywords and FailureKeywords must not overlap. Overlapping: {string.Join(", ", overlapping)}.",
                nameof(successKeywords));

        FromPattern = fromPattern;
        SubjectPattern = subjectPattern;
        SuccessKeywords = successKeywords;
        FailureKeywords = failureKeywords;
    }

    public override bool Equals(object? obj) =>
        obj is MailParsingRule other &&
        FromPattern == other.FromPattern &&
        SubjectPattern == other.SubjectPattern &&
        SuccessKeywords.SequenceEqual(other.SuccessKeywords, StringComparer.OrdinalIgnoreCase) &&
        FailureKeywords.SequenceEqual(other.FailureKeywords, StringComparer.OrdinalIgnoreCase);

    public override int GetHashCode() => HashCode.Combine(FromPattern, SubjectPattern);
}
