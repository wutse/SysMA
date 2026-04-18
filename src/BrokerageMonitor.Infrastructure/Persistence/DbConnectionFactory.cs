using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using System.Data;

namespace BrokerageMonitor.Infrastructure.Persistence;

/// <summary>
/// Creates and opens <see cref="IDbConnection"/> instances for SQLite.
/// Follows SRP: sole responsibility is connection lifecycle management.
/// Callers are responsible for disposing the returned connection.
/// </summary>
public sealed class DbConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    /// <summary>
    /// Initialises the factory from the "ConnectionStrings:BrokerageMonitor" entry
    /// in <paramref name="configuration"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the connection string is missing or empty.
    /// </exception>
    public DbConnectionFactory(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("BrokerageMonitor")
            ?? throw new InvalidOperationException(
                "Connection string 'BrokerageMonitor' is not configured in appsettings.json.");
    }

    /// <inheritdoc />
    public IDbConnection CreateConnection()
    {
        return new SqliteConnection(_connectionString);
    }
}
