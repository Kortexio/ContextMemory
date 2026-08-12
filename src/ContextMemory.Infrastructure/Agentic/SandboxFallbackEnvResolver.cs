using ContextMemory.Core.Agentic;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;

namespace ContextMemory.Infrastructure.Agentic;

/// <summary>
/// Merges <c>Env</c> from MCP credential store for conventional integrations
/// (<c>azure-monitor</c>, <c>github</c>, <c>git</c>) so sandbox fallback can reuse Admin secrets.
/// </summary>
public static class SandboxFallbackEnvResolver
{
    public static readonly string[] ConventionalIntegrationNames =
    [
        "azure-monitor",
        "github",
        "git"
    ];

    public static async Task<IReadOnlyDictionary<string, string>> ResolveAsync(
        string appId,
        AppRuntimeConfig runtimeConfig,
        IMcpCredentialStore credentialStore,
        CancellationToken cancellationToken = default)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var integration in runtimeConfig.Agentic.Tools.Integrations)
        {
            if (!integration.Enabled || !integration.IsConfigured)
                continue;

            if (!IsConventionalName(integration.Name))
                continue;

            if (integration.Env is not null)
            {
                foreach (var (key, value) in integration.Env)
                {
                    if (!string.IsNullOrWhiteSpace(key) && value is not null)
                        merged[key] = value;
                }
            }

            if (string.IsNullOrWhiteSpace(integration.CredentialRef))
                continue;

            var secret = await credentialStore
                .GetAsync(appId, integration.Name, integration.CredentialRef, cancellationToken)
                .ConfigureAwait(false);
            if (secret?.Env is null)
                continue;

            foreach (var (key, value) in secret.Env)
            {
                if (!string.IsNullOrWhiteSpace(key) && value is not null)
                    merged[key] = value;
            }
        }

        return merged;
    }

    public static bool IsConventionalName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        foreach (var conventional in ConventionalIntegrationNames)
        {
            if (string.Equals(name, conventional, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
