using ContextMemory.Admin.UI.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ContextMemory.Admin.UI;

public static class AdminUiServiceCollectionExtensions
{
    public static IServiceCollection AddContextMemoryAdminUi(this IServiceCollection services)
    {
        services.AddScoped<IAdminSettingsStorage, BrowserAdminSettingsStorage>();
        services.AddScoped<IChatTestSettingsStorage, BrowserChatTestSettingsStorage>();
        services.AddScoped<AdminSession>();
        services.AddHttpClient<AdminApiClient>();
        services.AddHttpClient<ChatClient>();
        return services;
    }
}
