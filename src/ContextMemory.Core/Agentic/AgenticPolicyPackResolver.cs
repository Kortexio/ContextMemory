using ContextMemory.Core.Agentic;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Microsoft.Extensions.Logging;

namespace ContextMemory.Core.Agentic;

public sealed class AgenticPolicyPackResolver : IAgenticPolicyPackResolver
{
    private readonly IAgenticPolicyCatalogStore _platformCatalog;
    private readonly IAgenticAppPolicyCatalogStore _appCatalog;
    private readonly ILogger<AgenticPolicyPackResolver> _logger;

    public AgenticPolicyPackResolver(
        IAgenticPolicyCatalogStore platformCatalog,
        IAgenticAppPolicyCatalogStore appCatalog,
        ILogger<AgenticPolicyPackResolver> logger)
    {
        _platformCatalog = platformCatalog;
        _appCatalog = appCatalog;
        _logger = logger;
    }

    public async Task<AppRuntimeConfig> ResolveAsync(
        AppRuntimeConfig runtimeConfig,
        CancellationToken cancellationToken = default)
    {
        await _platformCatalog.EnsureSeededAsync(cancellationToken).ConfigureAwait(false);
        var platform = await _platformCatalog.GetCatalogAsync(cancellationToken).ConfigureAwait(false);
        var app = string.IsNullOrWhiteSpace(runtimeConfig.AppId)
            ? new AgenticAppCatalogSnapshot()
            : await _appCatalog.GetCatalogAsync(runtimeConfig.AppId, cancellationToken).ConfigureAwait(false);

        var hasSelfHosted = runtimeConfig.Agentic.Tools.Execution.Any(e =>
            string.Equals(e.Type, "self-hosted-sandbox", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(e.SandboxEndpoint));

        var platformSkills = platform.Skills
            .Where(s => s.IsDefaultEnabled)
            .Where(s => hasSelfHosted
                        || !string.Equals(s.Id, "sandbox-facts-selfhosted", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var platformIds = new HashSet<string>(platformSkills.Select(s => s.Id), StringComparer.OrdinalIgnoreCase);

        var appSkills = app.Skills
            .Where(s => s.IsEnabled)
            .Where(s => !platformIds.Contains(s.Id))
            .Select(s => s.ToSkillDefinition())
            .ToList();

        var activeSkills = platformSkills
            .Concat(appSkills)
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var platformGuardrails = platform.Guardrails.Where(g => g.IsDefaultEnabled).ToList();
        var platformGuardrailIds = new HashSet<string>(
            platformGuardrails.Select(g => g.Id),
            StringComparer.OrdinalIgnoreCase);

        var appGuardrails = app.Guardrails
            .Where(g => g.IsEnabled)
            .Where(g => !platformGuardrailIds.Contains(g.Id))
            .Select(g => g.ToGuardrailDefinition())
            .ToList();

        var activeGuardrails = platformGuardrails
            .Concat(appGuardrails)
            .OrderBy(g => g.SortOrder)
            .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var kinds = new HashSet<string>(
            activeGuardrails.Select(g => g.Kind),
            StringComparer.OrdinalIgnoreCase);

        _logger.LogDebug(
            "Resolved agentic policy for {AppId}: {SkillCount} skills ({PlatformSkills} platform + {AppSkills} app), {GuardrailCount} guardrails",
            runtimeConfig.AppId,
            activeSkills.Count,
            platformSkills.Count,
            appSkills.Count,
            activeGuardrails.Count);

        return runtimeConfig with
        {
            ResolvedPolicy = new ResolvedAgenticPolicy
            {
                ActiveSkills = activeSkills,
                ActiveGuardrails = activeGuardrails,
                ActiveGuardrailKinds = kinds
            }
        };
    }
}
