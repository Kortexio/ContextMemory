using ContextMemory.Core.Contracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ContextMemory.Api.Hosting;

public sealed class AgenticCatalogSeedHostedService : IHostedService
{
    private readonly IAgenticPolicyCatalogStore _catalog;
    private readonly ILogger<AgenticCatalogSeedHostedService> _logger;

    public AgenticCatalogSeedHostedService(
        IAgenticPolicyCatalogStore catalog,
        ILogger<AgenticCatalogSeedHostedService> logger)
    {
        _catalog = catalog;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _catalog.EnsureSeededAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Agentic policy catalog ready");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to seed agentic policy catalog on startup");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
