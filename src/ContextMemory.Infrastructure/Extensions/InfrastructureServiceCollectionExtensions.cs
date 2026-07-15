using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Persistence;
using ContextMemory.Infrastructure.Agentic;
using ContextMemory.Infrastructure.Agentic.Mcp;
using ContextMemory.Infrastructure.Observability;
using ContextMemory.Infrastructure.Persistence.Postgres;
using ContextMemory.Infrastructure.Profile;
using ContextMemory.Infrastructure.RateLimiting;
using ContextMemory.Infrastructure.Session;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ContextMemory.Infrastructure.Extensions;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddContextMemoryFilePersistence(this IServiceCollection services)
    {
        services.AddSingleton<IAppRegistry, AppRegistry>();
        services.AddSingleton<IAppConfigStore, AppConfigStore>();
        services.AddSingleton<ISessionStore, FileSessionStore>();
        services.AddSingleton<IAgenticPendingStore, FileAgenticPendingStore>();
        return services;
    }

    public static IServiceCollection AddContextMemoryPostgresPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var provider = configuration.GetSection(ContextMemoryOptions.SectionName)["PersistenceProvider"];
        if (!PersistenceProviders.IsPostgres(provider))
            return services;

        var connectionString = configuration.GetConnectionString("ContextMemory");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ContextMemory:PersistenceProvider is Postgres but ConnectionStrings:ContextMemory is missing.");
        }

        var migrationsAssembly = typeof(ContextMemoryDbContext).Assembly.GetName().Name!;

        services.AddDbContextFactory<ContextMemoryDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsAssembly(migrationsAssembly)));

        services.AddSingleton<IAppRegistry, PostgresAppRegistry>();
        services.AddSingleton<IAppConfigStore, PostgresAppConfigStore>();
        services.AddSingleton<ISessionStore, PostgresSessionStore>();
        services.AddSingleton<IAgenticPendingStore, PostgresAgenticPendingStore>();
        services.AddSingleton<IPostgresHealthCheck, PostgresHealthCheck>();

        return services;
    }

    public static IServiceCollection AddContextMemoryInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<ITelemetryCollector, TelemetryCollector>();
        services.AddSingleton<IRateLimitService, RateLimitService>();

        services.AddHttpClient<AcaDynamicSessionsClient>(client => client.Timeout = TimeSpan.FromMinutes(5));
        services.AddHttpClient<SelfHostedSandboxClient>(client => client.Timeout = TimeSpan.FromMinutes(5));
        services.AddHttpClient<McpJsonRpcClient>(client => client.Timeout = TimeSpan.FromMinutes(2));
        services.AddHttpClient<McpOAuthTokenProvider>(client => client.Timeout = TimeSpan.FromSeconds(30));

        services.AddTransient<AcaExecutionToolExecutor>();
        services.AddTransient<SelfHostedGVisorExecutor>();
        services.AddTransient<McpToolExecutor>();
        services.AddTransient<IToolExecutor>(sp => sp.GetRequiredService<AcaExecutionToolExecutor>());
        services.AddTransient<IToolExecutor>(sp => sp.GetRequiredService<SelfHostedGVisorExecutor>());
        services.AddTransient<IToolExecutor>(sp => sp.GetRequiredService<McpToolExecutor>());

        services.AddSingleton<IMcpToolCatalog, McpToolCatalog>();

        return services;
    }
}

internal sealed class PostgresHealthCheck : IPostgresHealthCheck
{
    private readonly IDbContextFactory<ContextMemoryDbContext> _dbFactory;

    public PostgresHealthCheck(IDbContextFactory<ContextMemoryDbContext> dbFactory) =>
        _dbFactory = dbFactory;

    public async Task<bool> CanConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            return await db.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }
}
