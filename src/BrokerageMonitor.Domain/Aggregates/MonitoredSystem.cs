using BrokerageMonitor.Domain.ValueObjects;

namespace BrokerageMonitor.Domain.Aggregates;

/// <summary>
/// Aggregate Root representing a monitored brokerage system.
/// FR-031, FR-032, FR-036, FR-037
/// </summary>
public sealed class MonitoredSystem
{
    private readonly List<EmailAddress> _alertRecipients = [];

    public string SystemId { get; private init; }
    public string Name { get; private set; }
    public MarketSessionWindow MarketSession { get; private set; }
    public IReadOnlyList<EmailAddress> AlertRecipients => _alertRecipients.AsReadOnly();
    public bool IsMaintenanceActive { get; private set; }
    public string? MaintenanceOperator { get; private set; }
    public bool IsActive { get; private set; }

    // Required for Dapper materialization
    private MonitoredSystem()
    {
        SystemId = null!;
        Name = null!;
        MarketSession = null!;
    }

    public MonitoredSystem(
        string systemId,
        string name,
        MarketSessionWindow marketSession,
        IEnumerable<EmailAddress>? alertRecipients = null,
        bool isActive = true)
    {
        if (string.IsNullOrWhiteSpace(systemId))
            throw new ArgumentException("SystemId cannot be empty.", nameof(systemId));

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name cannot be empty.", nameof(name));

        ArgumentNullException.ThrowIfNull(marketSession);

        SystemId = systemId;
        Name = name;
        MarketSession = marketSession;
        IsActive = isActive;

        if (alertRecipients is not null)
            _alertRecipients.AddRange(alertRecipients);
    }

    public void Rename(string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
            throw new ArgumentException("Name cannot be empty.", nameof(newName));

        Name = newName;
    }

    public void UpdateMarketSession(MarketSessionWindow marketSession)
    {
        ArgumentNullException.ThrowIfNull(marketSession);
        MarketSession = marketSession;
    }

    public void SetAlertRecipients(IEnumerable<EmailAddress> recipients)
    {
        ArgumentNullException.ThrowIfNull(recipients);
        _alertRecipients.Clear();
        _alertRecipients.AddRange(recipients);
    }

    /// <summary>
    /// Enables maintenance mode. BI-009: operator name is required.
    /// </summary>
    public void ActivateMaintenance(string operatorName)
    {
        if (string.IsNullOrWhiteSpace(operatorName))
            throw new ArgumentException("OperatorName is required to activate maintenance mode (BI-009).", nameof(operatorName));

        IsMaintenanceActive = true;
        MaintenanceOperator = operatorName;
    }

    /// <summary>
    /// Disables maintenance mode. BI-009: operator name is required.
    /// </summary>
    public void DeactivateMaintenance(string operatorName)
    {
        if (string.IsNullOrWhiteSpace(operatorName))
            throw new ArgumentException("OperatorName is required to deactivate maintenance mode (BI-009).", nameof(operatorName));

        IsMaintenanceActive = false;
        MaintenanceOperator = null;
    }

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;
}
