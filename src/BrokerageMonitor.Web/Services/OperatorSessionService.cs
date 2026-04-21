namespace BrokerageMonitor.Web.Services;

/// <summary>
/// Circuit-scoped service that stores the operator's name for the current Blazor Server session.
/// Scoped lifetime ensures one instance per SignalR circuit (i.e., per browser tab).
/// FR-009: operator name must be captured before any manual action is permitted.
/// </summary>
public sealed class OperatorSessionService
{
    private string? _operatorName;

    /// <summary>
    /// Returns <c>true</c> when the operator has provided their name in this session.
    /// </summary>
    public bool IsIdentified => !string.IsNullOrWhiteSpace(_operatorName);

    /// <summary>
    /// The operator's name, or <c>null</c> if not yet provided.
    /// </summary>
    public string? OperatorName => _operatorName;

    /// <summary>
    /// Sets the operator name for this circuit session.
    /// </summary>
    /// <param name="name">The operator's display name (must not be empty).</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is null or whitespace.</exception>
    public void Identify(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Operator name cannot be empty.", nameof(name));

        _operatorName = name.Trim();
    }

    /// <summary>
    /// Clears the stored operator name (e.g., session sign-out).
    /// </summary>
    public void Clear() => _operatorName = null;
}
