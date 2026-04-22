using BrokerageMonitor.Application.Messaging;
using BrokerageMonitor.Domain.Aggregates;
using BrokerageMonitor.Domain.Events;
using BrokerageMonitor.Domain.Repositories;
using BrokerageMonitor.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace BrokerageMonitor.Application.Services;

/// <summary>
/// Processes incoming ZeroMQ mail-relay messages (FR-050, FR-051).
/// Algorithm (BI-016, BI-017):
///   1. Load all active components that have a <see cref="MailParsingRule"/>.
///   2. For each component, test FromPattern (exact/wildcard) AND SubjectPattern (substring).
///   3. On match: apply failure-first keyword evaluation against Subject + Body.
///   4. Map result to <see cref="ComponentStatus"/> (ScheduledJob: Completed/Failed;
///      Service: Normal/Error) and feed into the same state-machine pipeline as a heartbeat.
///   5. Log <c>ERR_MAIL_NO_RULE_MATCH</c> at WARNING when no component matches.
/// US-060.
/// </summary>
public sealed class MailChannelProcessor : IMailChannelProcessor
{
    internal const string ErrorCodeNoRuleMatch = "ERR_MAIL_NO_RULE_MATCH";

    private readonly IMonitoredComponentRepository _componentRepository;
    private readonly IComponentStateRepository _stateRepository;
    private readonly IComponentStateCache _stateCache;
    private readonly IDomainEventDispatcher _eventDispatcher;
    private readonly ILogger<MailChannelProcessor> _logger;

    public MailChannelProcessor(
        IMonitoredComponentRepository componentRepository,
        IComponentStateRepository stateRepository,
        IComponentStateCache stateCache,
        IDomainEventDispatcher eventDispatcher,
        ILogger<MailChannelProcessor> logger)
    {
        _componentRepository = componentRepository;
        _stateRepository = stateRepository;
        _stateCache = stateCache;
        _eventDispatcher = eventDispatcher;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task ProcessAsync(MailRelayMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var now = DateTimeOffset.UtcNow;

        // Raise domain event for audit / diagnostics regardless of match outcome.
        await _eventDispatcher.DispatchAsync(
            new MailChannelMessageReceived(
                message.From,
                message.Subject,
                message.Body,
                message.ReceivedAt,
                now),
            ct).ConfigureAwait(false);

        var components = await _componentRepository.GetAllActiveAsync(ct).ConfigureAwait(false);
        var matched = false;

        foreach (var component in components)
        {
            if (component.MailParsingRule is null)
                continue;

            var rule = component.MailParsingRule;

            if (!MatchesFrom(message.From, rule.FromPattern))
                continue;

            if (!MatchesSubject(message.Subject, rule.SubjectPattern))
                continue;

            // Pattern matched — apply failure-first keyword evaluation (BI-016).
            var isFailure = ContainsAnyKeyword(message.Subject, message.Body, rule.FailureKeywords);
            var isSuccess = !isFailure &&
                            ContainsAnyKeyword(message.Subject, message.Body, rule.SuccessKeywords);

            if (!isFailure && !isSuccess)
            {
                _logger.LogWarning(
                    "{ErrorCode}: Mail from '{From}' matched component '{ComponentId}' but no keyword matched. Discarding.",
                    ErrorCodeNoRuleMatch,
                    message.From,
                    component.ComponentId);
                continue;
            }

            var targetStatus = MapToStatus(component.ComponentType, isSuccess);

            await ApplyStatusAsync(component, targetStatus, now, ct).ConfigureAwait(false);

            matched = true;

            _logger.LogInformation(
                "MailChannelProcessor: mail from '{From}' matched component '{ComponentId}' → {Status}.",
                message.From,
                component.ComponentId,
                targetStatus);
        }

        if (!matched)
        {
            _logger.LogWarning(
                "{ErrorCode}: Mail from '{From}' subject '{Subject}' did not match any component rule.",
                ErrorCodeNoRuleMatch,
                message.From,
                message.Subject);
        }
    }

    // -------------------------------------------------------------------------
    // Pattern matching helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Matches sender address against a From pattern.
    /// Supports <c>*</c> (any sequence) and <c>?</c> (any single character) wildcards.
    /// Falls back to case-insensitive exact match when no wildcards are present.
    /// </summary>
    internal static bool MatchesFrom(string from, string pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return false;
        if (!pattern.Contains('*') && !pattern.Contains('?'))
            return string.Equals(from, pattern, StringComparison.OrdinalIgnoreCase);

        return WildcardMatch(from, pattern);
    }

    /// <summary>
    /// Matches subject against a pattern using case-insensitive substring search.
    /// </summary>
    internal static bool MatchesSubject(string subject, string pattern) =>
        !string.IsNullOrEmpty(pattern) &&
        subject.Contains(pattern, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns <see langword="true"/> if subject or body contains any of the given keywords
    /// (case-insensitive).
    /// </summary>
    internal static bool ContainsAnyKeyword(
        string subject,
        string body,
        IReadOnlyList<string> keywords)
    {
        foreach (var kw in keywords)
        {
            if (subject.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                body.Contains(kw, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Maps parse result to a <see cref="ComponentStatus"/> according to FR-050.
    /// ScheduledJob: success → Completed, failure → Failed.
    /// Service:      success → Normal,    failure → Error.
    /// </summary>
    internal static ComponentStatus MapToStatus(ComponentType componentType, bool isSuccess) =>
        componentType == ComponentType.ScheduledJob
            ? (isSuccess ? ComponentStatus.Completed : ComponentStatus.Failed)
            : (isSuccess ? ComponentStatus.Normal : ComponentStatus.Error);

    // -------------------------------------------------------------------------
    // State machine application (BI-017 — same pipeline as heartbeat)
    // -------------------------------------------------------------------------

    private async Task ApplyStatusAsync(
        MonitoredComponent component,
        ComponentStatus targetStatus,
        DateTimeOffset occurredAt,
        CancellationToken ct)
    {
        var currentState = _stateCache.GetState(component.ComponentId)
            ?? new ComponentState(component.ComponentId, ComponentStatus.Unknown);

        var previousStatus = currentState.Status;

        currentState.RecordHeartbeat(occurredAt);

        bool statusChanged = previousStatus != targetStatus;
        if (statusChanged)
        {
            currentState.UpdateStatus(targetStatus, occurredAt);
        }

        _stateCache.SetState(currentState);
        await _stateRepository.UpsertAsync(currentState, ct).ConfigureAwait(false);

        if (statusChanged)
        {
            await _eventDispatcher.DispatchAsync(
                new ComponentStatusChanged(
                    component.ComponentId,
                    component.SystemId,
                    previousStatus,
                    targetStatus,
                    occurredAt),
                ct).ConfigureAwait(false);
        }
    }

    // -------------------------------------------------------------------------
    // Wildcard matching (glob-style: * matches any sequence, ? matches any char)
    // -------------------------------------------------------------------------

    private static bool WildcardMatch(ReadOnlySpan<char> text, ReadOnlySpan<char> pattern)
    {
        // Dynamic programming approach for O(m*n) worst-case.
        int m = text.Length + 1;
        int n = pattern.Length + 1;

        // Use a two-row rolling array to keep memory constant.
        Span<bool> prev = stackalloc bool[n];
        Span<bool> curr = stackalloc bool[n];

        prev[0] = true;
        for (int j = 1; j < n; j++)
            prev[j] = prev[j - 1] && pattern[j - 1] == '*';

        for (int i = 1; i < m; i++)
        {
            curr[0] = false;
            for (int j = 1; j < n; j++)
            {
                if (pattern[j - 1] == '*')
                    curr[j] = curr[j - 1] || prev[j];
                else if (pattern[j - 1] == '?' ||
                         char.ToUpperInvariant(text[i - 1]) == char.ToUpperInvariant(pattern[j - 1]))
                    curr[j] = prev[j - 1];
                else
                    curr[j] = false;
            }

            var temp = prev;
            prev = curr;
            curr = temp;
        }

        return prev[n - 1];
    }
}
