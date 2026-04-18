using BrokerageMonitor.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace BrokerageMonitor.Infrastructure;

/// <summary>
/// Extension methods for registering Infrastructure services with the DI container.
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Registers the SQLite connection factory and database initialiser.
    /// The connection string is expected at "ConnectionStrings:BrokerageMonitor" in configuration.
    /// </summary>
    public static IServiceCollection AddPersistence(this IServiceCollection services)
    {
        services.AddSingleton<IDbConnectionFactory, DbConnectionFactory>();
        services.AddSingleton<DatabaseInitializer>();

        return services;
    }
}
