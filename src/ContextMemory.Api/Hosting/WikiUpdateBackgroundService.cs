using ContextMemory.Core.Contracts;
using ContextMemory.Core.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ContextMemory.Api.Hosting;

public sealed class WikiUpdateBackgroundService : BackgroundService
{
    private readonly WikiUpdateQueue _queue;
    private readonly WikiUpdateProcessor _processor;
    private readonly ILogger<WikiUpdateBackgroundService> _logger;

    public WikiUpdateBackgroundService(
        WikiUpdateQueue queue,
        WikiUpdateProcessor processor,
        ILogger<WikiUpdateBackgroundService> logger)
    {
        _queue = queue;
        _processor = processor;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Wiki update background service started.");

        await foreach (var job in _queue.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            await _processor.ProcessAsync(job, stoppingToken).ConfigureAwait(false);
        }
    }
}
