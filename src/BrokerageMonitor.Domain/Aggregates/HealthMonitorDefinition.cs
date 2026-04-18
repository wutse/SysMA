using BrokerageMonitor.Domain.ValueObjects;

namespace BrokerageMonitor.Domain.Aggregates;

/// <summary>
/// Aggregate Root representing a health monitoring definition for a system.
/// Replaces AggregateHealthRule. FR-041, FR-042, FR-046, FR-047
/// </summary>
public sealed class HealthMonitorDefinition
{
    private readonly List<WatchedComponent> _watchedComponents = [];
    private readonly List<EmailAddress> _emailRecipients = [];

    public Guid DefinitionId { get; private init; }
    public string SystemId { get; private init; }
    public string Name { get; private set; }
    public TimeOnly DeadlineTime { get; private set; }
    public HealthRuleSchedule Schedule { get; private set; }
    public IReadOnlyList<WatchedComponent> WatchedComponents => _watchedComponents.AsReadOnly();

    /// <summary>
    /// Independent email recipients. BI-010: completely separate from system alert recipients.
    /// </summary>
    public IReadOnlyList<EmailAddress> EmailRecipients => _emailRecipients.AsReadOnly();

    public string? TeamsWebhookUrl { get; private set; }

    /// <summary>
    /// Controls whether Email/Teams notifications are sent on failure (FR-014).
    /// Inbox writing always occurs regardless of this setting (FR-020).
    /// </summary>
    public bool SendOnFailure { get; private set; }

    public bool IsActive { get; private set; }

    // Required for Dapper materialization
    private HealthMonitorDefinition()
    {
        SystemId = null!;
        Name = null!;
        Schedule = null!;
    }

    public HealthMonitorDefinition(
        Guid definitionId,
        string systemId,
        string name,
        TimeOnly deadlineTime,
        HealthRuleSchedule schedule,
        IEnumerable<WatchedComponent> watchedComponents,
        IEnumerable<EmailAddress>? emailRecipients = null,
        string? teamsWebhookUrl = null,
        bool sendOnFailure = true,
        bool isActive = true)
    {
        if (definitionId == Guid.Empty)
            throw new ArgumentException("DefinitionId cannot be empty.", nameof(definitionId));

        if (string.IsNullOrWhiteSpace(systemId))
            throw new ArgumentException("SystemId cannot be empty.", nameof(systemId));

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name cannot be empty.", nameof(name));

        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(watchedComponents);

        var watchedList = watchedComponents.ToList();
        if (watchedList.Count == 0)
            throw new ArgumentException("WatchedComponents must not be empty (BI-014).", nameof(watchedComponents));

        DefinitionId = definitionId;
        SystemId = systemId;
        Name = name;
        DeadlineTime = deadlineTime;
        Schedule = schedule;
        SendOnFailure = sendOnFailure;
        TeamsWebhookUrl = teamsWebhookUrl;
        IsActive = isActive;

        _watchedComponents.AddRange(watchedList);

        if (emailRecipients is not null)
            _emailRecipients.AddRange(emailRecipients);
    }

    public void Rename(string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
            throw new ArgumentException("Name cannot be empty.", nameof(newName));

        Name = newName;
    }

    public void UpdateDeadlineTime(TimeOnly deadlineTime) => DeadlineTime = deadlineTime;

    public void UpdateSchedule(HealthRuleSchedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        Schedule = schedule;
    }

    /// <summary>
    /// BI-014: WatchedComponents must not be empty.
    /// </summary>
    public void SetWatchedComponents(IEnumerable<WatchedComponent> components)
    {
        ArgumentNullException.ThrowIfNull(components);

        var list = components.ToList();
        if (list.Count == 0)
            throw new ArgumentException("WatchedComponents must not be empty (BI-014).", nameof(components));

        _watchedComponents.Clear();
        _watchedComponents.AddRange(list);
    }

    public void SetEmailRecipients(IEnumerable<EmailAddress> recipients)
    {
        ArgumentNullException.ThrowIfNull(recipients);
        _emailRecipients.Clear();
        _emailRecipients.AddRange(recipients);
    }

    public void SetTeamsWebhookUrl(string? url) => TeamsWebhookUrl = url;

    public void SetSendOnFailure(bool sendOnFailure) => SendOnFailure = sendOnFailure;

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;
}
