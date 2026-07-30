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
}

public interface IAgenticPolicyPackResolver
{
    Task<AppRuntimeConfig> ResolveAsync(
        AppRuntimeConfig runtimeConfig,
        CancellationToken cancellationToken = default);
}
