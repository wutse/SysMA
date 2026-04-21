namespace BrokerageMonitor.Application.Startup;

/// <summary>
/// POCO bound to the <c>Systems[*]</c> array in <c>appsettings.json</c>.
/// Used by <see cref="AppSettingsImporter"/> for initial data seeding (FR-031).
/// </summary>
public sealed class SystemConfig
{
    public string SystemId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string MarketSessionStart { get; set; } = "09:00";
    public string MarketSessionEnd { get; set; } = "13:30";
    public List<string> AlertRecipients { get; set; } = [];
    public List<ComponentConfig> Components { get; set; } = [];
}

/// <summary>
/// POCO for a component entry within a <see cref="SystemConfig"/>.
/// </summary>
public sealed class ComponentConfig
{
    public string ComponentId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>"Service" or "ScheduledJob".</summary>
    public string ComponentType { get; set; } = "Service";

    public string ZeroMQTopic { get; set; } = string.Empty;
    public int HeartbeatTimeoutSeconds { get; set; } = 60;
    public string? CronExpression { get; set; }
    public bool IsActive { get; set; } = true;
}
