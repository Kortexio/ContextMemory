using ContextMemory.Admin.UI.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ContextMemory.Admin.UI;

public static class AdminUiServiceCollectionExtensions
{
    public static IServiceCollection AddContextMemoryAdminUi(this IServiceCollection services)
    {
        services.AddOptions<AdminUiOptions>()
            .BindConfiguration(AdminUiOptions.SectionName)
            .PostConfigure<Microsoft.Extensions.Configuration.IConfiguration>((options, configuration) =>
            {
                // Backward-compatible flat key used by Admin.Web appsettings
                if (string.IsNullOrWhiteSpace(options.DefaultApiBaseUrl))
                    options.DefaultApiBaseUrl = configuration["ApiBaseUrl"] ?? "http://localhost:5100";
                if (string.IsNullOrWhiteSpace(options.PublicApiBaseUrl))
                    options.PublicApiBaseUrl = options.DefaultApiBaseUrl;
            });

        services.AddScoped<IAdminSettingsStorage, BrowserAdminSettingsStorage>();
        services.AddScoped<IChatTestSettingsStorage, BrowserChatTestSettingsStorage>();
        services.AddScoped<AdminSession>();
        services.AddSingleton<AdminDocsMarkdownRenderer>();
        services.AddSingleton<IAdminDocsService, AdminDocsService>();
        services.AddHttpClient<AdminApiClient>();
        services.AddHttpClient<ChatClient>();
        return services;
    }
}
