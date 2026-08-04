using ContextMemory.Core.Agentic;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Contracts;

public interface IAgenticPolicyCatalogStore
{
    Task<AgenticCatalogSnapshot> GetCatalogAsync(CancellationToken cancellationToken = default);

    Task EnsureSeededAsync(CancellationToken cancellationToken = default);

    Task<AgenticSkillDefinition?> GetSkillAsync(string id, CancellationToken cancellationToken = default);

    Task<AgenticSkillDefinition> UpsertSkillAsync(
        AgenticSkillDefinition skill,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteSkillAsync(string id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgenticGuardrailDefinition>> ListGuardrailsAsync(
        CancellationToken cancellationToken = default);

    Task<AgenticGuardrailDefinition?> GetGuardrailAsync(string id, CancellationToken cancellationToken = default);

    Task<AgenticGuardrailDefinition> UpsertGuardrailAsync(
        AgenticGuardrailDefinition guardrail,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteGuardrailAsync(string id, CancellationToken cancellationToken = default);
}

/// <summary>Per-app skills and guardrails (additive to the platform catalog).</summary>
public interface IAgenticAppPolicyCatalogStore
{
    Task<AgenticAppCatalogSnapshot> GetCatalogAsync(string appId, CancellationToken cancellationToken = default);

    Task<AgenticAppSkillDefinition?> GetSkillAsync(
        string appId,
        string id,
        CancellationToken cancellationToken = default);

    Task<AgenticAppSkillDefinition> UpsertSkillAsync(
        AgenticAppSkillDefinition skill,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteSkillAsync(string appId, string id, CancellationToken cancellationToken = default);

    Task<AgenticAppGuardrailDefinition?> GetGuardrailAsync(
        string appId,
        string id,
        CancellationToken cancellationToken = default);

    Task<AgenticAppGuardrailDefinition> UpsertGuardrailAsync(
        AgenticAppGuardrailDefinition guardrail,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteGuardrailAsync(string appId, string id, CancellationToken cancellationToken = default);
}

public interface IAgenticPolicyPackResolver
{
    Task<AppRuntimeConfig> ResolveAsync(
        AppRuntimeConfig runtimeConfig,
        CancellationToken cancellationToken = default);
}
