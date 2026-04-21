namespace BrokerageMonitor.Application.DTOs;

/// <summary>
/// Represents the outcome of a SignalR Hub client-to-server invocation.
/// Provides a typed payload on success and an error message on failure.
/// US-035, EP-007.
/// </summary>
/// <typeparam name="T">Type of the successful payload.</typeparam>
public sealed record Result<T>
{
    /// <summary>Whether the operation succeeded.</summary>
    public bool IsSuccess { get; init; }

    /// <summary>The result payload when <see cref="IsSuccess"/> is <c>true</c>.</summary>
    public T? Value { get; init; }

    /// <summary>Human-readable error description when <see cref="IsSuccess"/> is <c>false</c>.</summary>
    public string? Error { get; init; }

    /// <summary>Creates a successful result with the given <paramref name="value"/>.</summary>
    public static Result<T> Ok(T value) => new() { IsSuccess = true, Value = value };

    /// <summary>Creates a failed result with the given <paramref name="error"/> message.</summary>
    public static Result<T> Fail(string error) => new() { IsSuccess = false, Error = error };
}
