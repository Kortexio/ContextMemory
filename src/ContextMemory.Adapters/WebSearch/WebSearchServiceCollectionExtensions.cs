using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ContextMemory.Adapters.WebSearch;

public static class WebSearchServiceCollectionExtensions
{
    public static IServiceCollection AddContextMemoryWebSearch(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ContextMemoryOptions>(configuration.GetSection(ContextMemoryOptions.SectionName));

        var timeoutSeconds = Math.Clamp(
            configuration.GetValue<int?>($"{ContextMemoryOptions.SectionName}:WebSearch:RequestTimeoutSeconds") ?? 15,
            5,
            60);
        var timeout = TimeSpan.FromSeconds(timeoutSeconds);

        services.AddHttpClient<TavilyWebSearchProvider>(client =>
        {
            client.BaseAddress = new Uri("https://api.tavily.com/");
            client.Timeout = timeout;
        });

        services.AddHttpClient<BraveWebSearchProvider>(client =>
        {
            client.BaseAddress = new Uri("https://api.search.brave.com/");
            client.Timeout = timeout;
        });

        services.AddSingleton<IWebSearchProvider>(sp => sp.GetRequiredService<TavilyWebSearchProvider>());
        services.AddSingleton<IWebSearchProvider>(sp => sp.GetRequiredService<BraveWebSearchProvider>());
        services.AddSingleton<IWebSearchProviderResolver, WebSearchProviderResolver>();

        return services;
    }
}
