using ContextMemory.Core.Contracts;

namespace ContextMemory.Core.Contracts;

/// <summary>
/// Resolves the configured web search provider for a tenant.
/// </summary>
public interface IWebSearchProviderResolver
{
    bool TryResolve(string? providerName, out IWebSearchProvider? provider);

    IReadOnlyList<IWebSearchProvider> Providers { get; }
}
