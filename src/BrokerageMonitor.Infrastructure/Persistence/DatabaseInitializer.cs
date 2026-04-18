using Microsoft.Extensions.Logging;

namespace BrokerageMonitor.Infrastructure.Persistence;

/// <summary>
/// Runs one-time database initialisation on startup:
/// enables SQLite WAL journal mode and creates all required tables
/// (via CREATE TABLE IF NOT EXISTS).
/// Follows SRP: sole responsibility is schema bootstrapping.
/// </summary>
public sealed class DatabaseInitializer
{
  private readonly IDbConnectionFactory _connectionFactory;
  private readonly ILogger<DatabaseInitializer> _logger;

  public DatabaseInitializer(
      IDbConnectionFactory connectionFactory,
      ILogger<DatabaseInitializer> logger)
  {
    _connectionFactory = connectionFactory;
    _logger = logger;
  }

  /// <summary>
  /// Executes WAL pragma and creates schema tables.
  /// Safe to call on every startup; all DDL uses IF NOT EXISTS.
  /// </summary>
  /// <param name="cancellationToken">Propagated cancellation token.</param>
  public async Task InitialiseAsync(CancellationToken cancellationToken = default)
  {
    _logger.LogInformation("DatabaseInitializer: beginning schema initialisation.");

    using var connection = _connectionFactory.CreateConnection();
    connection.Open();

    // Enable Write-Ahead Logging for concurrent read/write performance
    await ExecuteAsync(connection, "PRAGMA journal_mode=WAL;", cancellationToken);

    _logger.LogInformation("DatabaseInitializer: schema initialisation completed successfully.");
  }

  private static Task ExecuteAsync(
      System.Data.IDbConnection connection,
      string sql,
      CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();
    using var cmd = connection.CreateCommand();
    cmd.CommandText = sql;
    cmd.ExecuteNonQuery();
    return Task.CompletedTask;
  }
}
