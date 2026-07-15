using ContextMemory.Core.Contracts;

namespace ContextMemory.Adapters.WebSearch;

public sealed class WebSearchProviderResolver : IWebSearchProviderResolver
{
    private readonly IReadOnlyDictionary<string, IWebSearchProvider> _providers;

    public WebSearchProviderResolver(IEnumerable<IWebSearchProvider> providers)
    {
        _providers = providers.ToDictionary(
            p => p.ProviderName,
            StringComparer.OrdinalIgnoreCase);
        Providers = _providers.Values.ToList();
    }

    public IReadOnlyList<IWebSearchProvider> Providers { get; }

    public bool TryResolve(string? providerName, out IWebSearchProvider? provider)
    {
        var key = string.IsNullOrWhiteSpace(providerName) ? "tavily" : providerName.Trim();
        if (_providers.TryGetValue(key, out var resolved))
        {
            provider = resolved;
            return true;
        }

        provider = null;
        return false;
    }
}
