using System.Data;

namespace BrokerageMonitor.Infrastructure.Persistence;

/// <summary>
/// Abstracts SQLite connection creation to enable DI and unit-test substitution.
/// </summary>
public interface IDbConnectionFactory
{
  /// <summary>
  /// Creates and returns a new, unopened database connection.
  /// The caller is responsible for opening and disposing the connection.
  /// </summary>
  IDbConnection CreateConnection();
}
