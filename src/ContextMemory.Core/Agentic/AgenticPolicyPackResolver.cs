using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Microsoft.Extensions.Logging;

namespace ContextMemory.Core.Agentic;

public sealed class AgenticPolicyPackResolver : IAgenticPolicyPackResolver
{
    private readonly IAgenticPolicyCatalogStore _catalog;
    private readonly ILogger<AgenticPolicyPackResolver> _logger;

    public AgenticPolicyPackResolver(
        IAgenticPolicyCatalogStore catalog,
        ILogger<AgenticPolicyPackResolver> logger)
    {
        _catalog = catalog;
        _logger = logger;
    }

    public async Task<AppRuntimeConfig> ResolveAsync(
        AppRuntimeConfig runtimeConfig,
        CancellationToken cancellationToken = default)
    {
        await _catalog.EnsureSeededAsync(cancellationToken).ConfigureAwait(false);
        var snapshot = await _catalog.GetCatalogAsync(cancellationToken).ConfigureAwait(false);

        var packs = runtimeConfig.Agentic.PolicyPacks;
        var skillIds = ResolveIds(
            packs.EnabledSkillIds,
            snapshot.Skills.Where(s => s.IsDefaultEnabled).Select(s => s.Id));
        var guardrailIds = ResolveIds(
            packs.EnabledGuardrailIds,
            snapshot.Guardrails.Where(g => g.IsDefaultEnabled).Select(g => g.Id));

        var hasSelfHosted = runtimeConfig.Agentic.Tools.Execution.Any(e =>
            string.Equals(e.Type, "self-hosted-sandbox", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(e.SandboxEndpoint));

        var activeSkills = snapshot.Skills
            .Where(s => skillIds.Contains(s.Id))
            .Where(s => hasSelfHosted
                        || !string.Equals(s.Id, "sandbox-facts-selfhosted", StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var activeGuardrails = snapshot.Guardrails
            .Where(g => guardrailIds.Contains(g.Id))
            .OrderBy(g => g.SortOrder)
            .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var kinds = new HashSet<string>(
            activeGuardrails.Select(g => g.Kind),
            StringComparer.OrdinalIgnoreCase);

        _logger.LogDebug(
            "Resolved agentic policy for {AppId}: {SkillCount} skills, {GuardrailCount} guardrails",
            runtimeConfig.AppId,
            activeSkills.Count,
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

    private static HashSet<string> ResolveIds(IReadOnlyList<string>? explicitIds, IEnumerable<string> defaults)
    {
        if (explicitIds is null)
            return new HashSet<string>(defaults, StringComparer.OrdinalIgnoreCase);

        return new HashSet<string>(
            explicitIds.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()),
            StringComparer.OrdinalIgnoreCase);
    }
}
