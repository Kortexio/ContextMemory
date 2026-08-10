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
using ContextMemory.Infrastructure.Wiki;
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
        services.AddSingleton<ISessionArtifactStore, FileSessionArtifactStore>();
        services.AddSingleton<IAgenticPendingStore, FileAgenticPendingStore>();
        services.AddSingleton<IGlobalWikiStore, FileGlobalWikiStore>();
        services.AddSingleton<IMcpCredentialStore, FileMcpCredentialStore>();
        services.AddSingleton<IMcpCatalogStore, FileMcpCatalogStore>();
        services.AddSingleton<IAgenticPolicyCatalogStore, FileAgenticPolicyCatalogStore>();
        services.AddSingleton<IAgenticAppPolicyCatalogStore, FileAgenticAppPolicyCatalogStore>();
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
        services.AddSingleton<ISessionArtifactStore, PostgresSessionArtifactStore>();
        services.AddSingleton<IAgenticPendingStore, PostgresAgenticPendingStore>();
        services.AddSingleton<IGlobalWikiStore, PostgresGlobalWikiStore>();
        services.AddSingleton<IMcpCredentialStore, PostgresMcpCredentialStore>();
        services.AddSingleton<IMcpCatalogStore, PostgresMcpCatalogStore>();
        services.AddSingleton<IAgenticPolicyCatalogStore, PostgresAgenticPolicyCatalogStore>();
        services.AddSingleton<IAgenticAppPolicyCatalogStore, PostgresAgenticAppPolicyCatalogStore>();
        services.AddSingleton<IPostgresHealthCheck, PostgresHealthCheck>();

        return services;
    }

    public static IServiceCollection AddContextMemoryInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<ITelemetryCollector, TelemetryCollector>();
        services.AddSingleton<IRateLimitService, RateLimitService>();
        services.AddSingleton<IPlatformDefaultsStore, FilePlatformDefaultsStore>();

        services.AddHttpClient<AcaDynamicSessionsClient>(client => client.Timeout = TimeSpan.FromMinutes(5));
        services.AddHttpClient<SelfHostedSandboxClient>(client => client.Timeout = TimeSpan.FromMinutes(5));
        services.AddHttpClient<McpJsonRpcClient>(client => client.Timeout = TimeSpan.FromMinutes(2));
        services.AddHttpClient<McpOAuthTokenProvider>(client => client.Timeout = TimeSpan.FromSeconds(30));
        // Must exceed typical Zuora Data Query / REMOTE_MCP_TIMEOUT_MS values (often 3–10 min).
        services.AddHttpClient("McpRuntime", client => client.Timeout = TimeSpan.FromMinutes(15));
        services.AddSingleton<McpStdioClient>();

        services.AddTransient<AcaExecutionToolExecutor>();
        services.AddTransient<SelfHostedGVisorExecutor>();
        services.AddTransient<McpToolExecutor>();
        services.AddTransient<GlobalWikiToolExecutor>();
        services.AddTransient<SessionDiscoveryToolExecutor>();
        services.AddTransient<DelegateTaskToolExecutor>();
        services.AddTransient<HttpToolsExecutor>();
        services.AddTransient<VisionToolsExecutor>();
        services.AddTransient<BrowserToolsExecutor>();
        services.AddTransient<DocumentToolsExecutor>();
        services.AddTransient<CanvasToolsExecutor>();
        services.AddTransient<IToolExecutor>(sp => sp.GetRequiredService<AcaExecutionToolExecutor>());
        services.AddTransient<IToolExecutor>(sp => sp.GetRequiredService<SelfHostedGVisorExecutor>());
        services.AddTransient<IToolExecutor>(sp => sp.GetRequiredService<McpToolExecutor>());
        services.AddTransient<IToolExecutor>(sp => sp.GetRequiredService<GlobalWikiToolExecutor>());
        services.AddTransient<IToolExecutor>(sp => sp.GetRequiredService<HttpToolsExecutor>());
        services.AddTransient<ISessionScopedToolExecutor>(sp => sp.GetRequiredService<SessionDiscoveryToolExecutor>());
        services.AddTransient<ISessionScopedToolExecutor>(sp => sp.GetRequiredService<DelegateTaskToolExecutor>());
        services.AddTransient<ISessionScopedToolExecutor>(sp => sp.GetRequiredService<VisionToolsExecutor>());
        services.AddTransient<ISessionScopedToolExecutor>(sp => sp.GetRequiredService<BrowserToolsExecutor>());
        services.AddTransient<ISessionScopedToolExecutor>(sp => sp.GetRequiredService<DocumentToolsExecutor>());
        services.AddTransient<ISessionScopedToolExecutor>(sp => sp.GetRequiredService<CanvasToolsExecutor>());

        services.AddHttpClient(HttpToolsExecutor.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromMinutes(2);
            // Do not follow redirects automatically — we re-validate hosts.
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = false
        });

        services.AddSingleton<IMcpToolSelector, McpToolSelector>();
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
