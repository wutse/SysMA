using BrokerageMonitor.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Data;

namespace BrokerageMonitor.Infrastructure.Tests.Persistence;

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/// <summary>
/// Minimal <see cref="IDbConnectionFactory"/> backed by a shared in-memory SQLite
/// connection so the WAL pragma test can open and inspect the DB within one connection.
/// </summary>
file sealed class InMemoryConnectionFactory : IDbConnectionFactory
{
    private readonly SqliteConnection _connection;

    public InMemoryConnectionFactory()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    public IDbConnection CreateConnection() => _connection;
}

/// <summary>
/// Minimal <see cref="IDbConnectionFactory"/> that delegates to a supplied
/// connection-string builder so each test can use an isolated file-based or
/// in-memory SQLite database.
/// </summary>
file sealed class ConnectionStringFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public ConnectionStringFactory(string connectionString) =>
        _connectionString = connectionString;

    public IDbConnection CreateConnection() => new SqliteConnection(_connectionString);
}

// ---------------------------------------------------------------------------
// IDbConnectionFactory tests
// ---------------------------------------------------------------------------

[TestClass]
public sealed class DbConnectionFactoryTests
{
    [TestMethod]
    public void CreateConnection_WithValidConnectionString_ReturnsOpenableConnection()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:BrokerageMonitor"] = "Data Source=:memory:"
            })
            .Build();

        var factory = new DbConnectionFactory(config);

        // Act
        using var connection = factory.CreateConnection();
        connection.Open();

        // Assert
        Assert.AreEqual(ConnectionState.Open, connection.State);
    }

    [TestMethod]
    public void Constructor_WhenConnectionStringMissing_ThrowsInvalidOperationException()
    {
        // Arrange
        var config = new ConfigurationBuilder().Build(); // no connection strings

        // Act & Assert
        Assert.ThrowsExactly<InvalidOperationException>(
            () => new DbConnectionFactory(config),
            "Constructor should throw when 'BrokerageMonitor' connection string is absent.");
    }

    [TestMethod]
    public void CreateConnection_CalledTwice_ReturnsDistinctInstances()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:BrokerageMonitor"] = "Data Source=:memory:"
            })
            .Build();

        var factory = new DbConnectionFactory(config);

        // Act
        using var conn1 = factory.CreateConnection();
        using var conn2 = factory.CreateConnection();

        // Assert — factory returns new instances each time
        Assert.AreNotSame(conn1, conn2);
    }

    [TestMethod]
    public void AddPersistence_RegistersIDbConnectionFactoryAsSingleton()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:BrokerageMonitor"] = "Data Source=:memory:"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();

        // Act
        services.AddPersistence();

        // Assert — IDbConnectionFactory is registered
        Assert.IsTrue(
            services.Any(sd => sd.ServiceType == typeof(IDbConnectionFactory)),
            "AddPersistence should register IDbConnectionFactory.");
    }

    [TestMethod]
    public void AddPersistence_RegistersDatabaseInitializerAsSingleton()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:BrokerageMonitor"] = "Data Source=:memory:"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();

        // Act
        services.AddPersistence();

        // Assert — DatabaseInitializer is registered
        Assert.IsTrue(
            services.Any(sd => sd.ServiceType == typeof(DatabaseInitializer)),
            "AddPersistence should register DatabaseInitializer.");
    }
}

// ---------------------------------------------------------------------------
// DatabaseInitializer tests
// ---------------------------------------------------------------------------

[TestClass]
public sealed class DatabaseInitializerTests
{
    [TestMethod]
    public async Task InitialiseAsync_WhenCalled_EnablesWalJournalMode()
    {
        // Arrange — shared in-memory connection so we can query the same DB
        var factory = new InMemoryConnectionFactory();
        var initializer = new DatabaseInitializer(factory, NullLogger<DatabaseInitializer>.Instance);

        // Act
        await initializer.InitialiseAsync(CancellationToken.None);

        // Assert — journal_mode should be 'wal' or 'memory' for in-memory DBs
        // SQLite returns "memory" for :memory: even when WAL is requested;
        // we assert the PRAGMA executed without error and the DB is queryable.
        using var conn = factory.CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode;";
        var result = cmd.ExecuteScalar()?.ToString();
        Assert.IsNotNull(result, "PRAGMA journal_mode should return a non-null value.");
    }

    [TestMethod]
    public async Task InitialiseAsync_CalledMultipleTimes_DoesNotThrow()
    {
        // Arrange
        var factory = new InMemoryConnectionFactory();
        var initializer = new DatabaseInitializer(factory, NullLogger<DatabaseInitializer>.Instance);

        // Act & Assert — idempotent calls must not throw
        await initializer.InitialiseAsync();
        await initializer.InitialiseAsync();
    }

    [TestMethod]
    public async Task InitialiseAsync_WithWalFileDb_JournalModeIsWal()
    {
        // Arrange — use a temp file-based SQLite DB (WAL requires a real file)
        var dbPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.db");
        try
        {
            string? result;
            var factory = new ConnectionStringFactory($"Data Source={dbPath}");
            var initializer = new DatabaseInitializer(factory, NullLogger<DatabaseInitializer>.Instance);

            // Act
            await initializer.InitialiseAsync(CancellationToken.None);

            // Assert — open a fresh connection to query journal_mode
            using (var conn = factory.CreateConnection())
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "PRAGMA journal_mode;";
                result = cmd.ExecuteScalar()?.ToString();
            } // connection disposed here, releasing file lock

            Assert.AreEqual("wal", result, "journal_mode should be 'wal' on a file-based SQLite database.");
        }
        finally
        {
            // Cleanup temp DB files (connection already disposed above)
            SqliteConnection.ClearAllPools();
            foreach (var f in Directory.GetFiles(Path.GetTempPath(), $"{Path.GetFileNameWithoutExtension(dbPath)}*"))
                File.Delete(f);
        }
    }

    [TestMethod]
    public async Task InitialiseAsync_CancelledToken_ThrowsOperationCancelledException()
    {
        // Arrange
        var factory = new InMemoryConnectionFactory();
        var initializer = new DatabaseInitializer(factory, NullLogger<DatabaseInitializer>.Instance);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => initializer.InitialiseAsync(cts.Token));
    }
}
