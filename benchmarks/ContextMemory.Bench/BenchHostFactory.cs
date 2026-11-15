using ContextMemory.Api.Extensions;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using ContextMemory.Infrastructure.Agentic;
using ContextMemory.Infrastructure.Agentic.Mcp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ContextMemory.Bench;

/// <summary>In-process CM host (no HTTP) for harness A/B against an external OpenAI-compatible /v1.</summary>
internal sealed class BenchHost : IAsyncDisposable
{
    private readonly IHost _host;
    private readonly string _dataRoot;

    public IServiceProvider Services => _host.Services;

    public BenchStickyWikiGate StickyWiki => Services.GetRequiredService<BenchStickyWikiGate>();

    private BenchHost(IHost host, string dataRoot)
    {
        _host = host;
        _dataRoot = dataRoot;
    }

    public static async Task<BenchHost> StartAsync(
        string dataRoot,
        string llmEndpoint,
        string model,
        string? apiKey,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(dataRoot);
        llmEndpoint = llmEndpoint.TrimEnd('/');

        var values = new Dictionary<string, string?>
        {
            ["ContextMemory:PersistenceProvider"] = "File",
            ["ContextMemory:DataPath"] = dataRoot,
            ["ContextMemory:MasterKey"] = "bench-master-key",
            ["ContextMemory:DefaultLlmModel"] = model,
            ["ContextMemory:LlmBackend"] = "openai-compatible",
            ["ContextMemory:LlmEndpoint"] = llmEndpoint,
            ["ContextMemory:OpenAiEndpoint"] = llmEndpoint,
            ["ContextMemory:OllamaRequestTimeoutSeconds"] = "600",
            ["ContextMemory:Apps:bench-app:ApiKey"] = "bench-api-key",
            ["ContextMemory:Apps:bench-app:SystemPrompt"] = "You are a careful company assistant. Use tools for live facts.",
            ["ContextMemory:Apps:bench-app:DefaultLanguage"] = "en-US",
            ["ContextMemory:Apps:bench-app:LlmModel"] = model,
            ["ContextMemory:Apps:bench-app:LlmBackend"] = "openai-compatible",
            ["ContextMemory:Apps:bench-app:LlmEndpoint"] = llmEndpoint
        };
        if (!string.IsNullOrWhiteSpace(apiKey))
            values["ContextMemory:OpenAiApiKey"] = apiKey;

        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(values);
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.Configure<ContextMemoryOptions>(options =>
        {
            builder.Configuration.GetSection(ContextMemoryOptions.SectionName).Bind(options);
            options.ContentRootPath = dataRoot;
        });
        builder.Services.AddContextMemory(builder.Configuration);
        ReplaceWikiExecutorWithSticky(builder.Services);

        var host = builder.Build();
        await host.StartAsync(cancellationToken).ConfigureAwait(false);
        return new BenchHost(host, dataRoot);
    }

    /// <summary>
    /// Swap the bare <see cref="GlobalWikiToolExecutor"/> IToolExecutor registration for the sticky wrapper (CM4).
    /// </summary>
    private static void ReplaceWikiExecutorWithSticky(IServiceCollection services)
    {
        services.AddSingleton<BenchStickyWikiGate>();

        var existing = services.Where(d => d.ServiceType == typeof(IToolExecutor)).ToList();
        foreach (var d in existing)
            services.Remove(d);

        services.AddTransient<IToolExecutor>(sp => sp.GetRequiredService<AcaExecutionToolExecutor>());
        services.AddTransient<IToolExecutor>(sp => sp.GetRequiredService<SelfHostedGVisorExecutor>());
        services.AddTransient<IToolExecutor>(sp => sp.GetRequiredService<McpToolExecutor>());
        services.AddTransient<IToolExecutor>(sp => new BenchStickyWikiExecutor(
            sp.GetRequiredService<GlobalWikiToolExecutor>(),
            sp.GetRequiredService<BenchStickyWikiGate>()));
        services.AddTransient<IToolExecutor>(sp => sp.GetRequiredService<HttpToolsExecutor>());
    }

    public async ValueTask DisposeAsync()
    {
        try { await _host.StopAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
        catch { /* ignore */ }
        _host.Dispose();
        if (Directory.Exists(_dataRoot))
        {
            try { Directory.Delete(_dataRoot, recursive: true); }
            catch { /* best effort */ }
        }
    }
}
