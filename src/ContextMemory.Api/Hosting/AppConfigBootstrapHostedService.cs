using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ContextMemory.Api.Hosting;

public sealed class AppConfigBootstrapHostedService : IHostedService
{
    private readonly IAppConfigStore _appConfigStore;
    private readonly ContextMemoryOptions _options;
    private readonly IHostEnvironment _environment;

    public AppConfigBootstrapHostedService(
        IAppConfigStore appConfigStore,
        IOptions<ContextMemoryOptions> options,
        IHostEnvironment environment)
    {
        _appConfigStore = appConfigStore;
        _options = options.Value;
        _environment = environment;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var devWebSearchKeyConfigured = _environment.IsDevelopment()
            && (!string.IsNullOrWhiteSpace(_options.WebSearch.TavilyApiKey)
                || !string.IsNullOrWhiteSpace(_options.WebSearch.BraveApiKey));

        foreach (var (appId, entry) in _options.Apps)
        {
            var seed = new AppRuntimeConfig
            {
                AppId = appId,
                BasePersona = entry.SystemPrompt,
                FormatRules = GetDefaultFormatRules(),
                DefaultLanguage = entry.DefaultLanguage,
                LlmModel = string.IsNullOrWhiteSpace(entry.LlmModel) ? _options.DefaultLlmModel : entry.LlmModel,
                MaxHistoryMessages = entry.MaxHistoryMessages > 0
                    ? entry.MaxHistoryMessages
                    : _options.MaxHistoryMessages,
                WebSearch = devWebSearchKeyConfigured
                    ? new WebSearchConfig
                    {
                        Enabled = true,
                        Mode = "heuristic",
                        Provider = string.IsNullOrWhiteSpace(_options.WebSearch.DefaultProvider)
                            ? "tavily"
                            : _options.WebSearch.DefaultProvider,
                        PersistToWiki = true,
                        LogWebSearch = true
                    }
                    : WebSearchConfig.Disabled
            };

            _appConfigStore.EnsureProfileExists(appId, seed);

            if (_environment.IsDevelopment())
            {
                var current = _appConfigStore.GetConfig(appId);
                if (current.LlmModel.Equals("qwen3.5:9b", StringComparison.OrdinalIgnoreCase)
                    && !seed.LlmModel.Equals("qwen3.5:9b", StringComparison.OrdinalIgnoreCase))
                {
                    await _appConfigStore.UpdateAsync(
                            appId,
                            new AppConfigPatchRequest { LlmModel = seed.LlmModel },
                            cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            if (devWebSearchKeyConfigured)
            {
                var current = _appConfigStore.GetConfig(appId);
                if (!current.WebSearch.IsActive)
                {
                    await _appConfigStore.UpdateAsync(
                            appId,
                            new AppConfigPatchRequest
                            {
                                WebSearch = new WebSearchConfigPatch
                                {
                                    Enabled = true,
                                    Mode = "heuristic",
                                    Provider = seed.WebSearch.Provider,
                                    PersistToWiki = true,
                                    LogWebSearch = true
                                }
                            },
                            cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            await _appConfigStore.ReloadAsync(appId, cancellationToken).ConfigureAwait(false);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static string GetDefaultFormatRules() =>
        """
        - Usa markdown quando apropriado.
        - Sê claro e estruturado.
        """;
}
