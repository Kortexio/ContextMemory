using System.Text.Json;
using ContextMemory.Admin.UI.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.JSInterop;

namespace ContextMemory.Admin.UI.Services;

public sealed class BrowserAdminSettingsStorage(IJSRuntime js, IConfiguration configuration) : IAdminSettingsStorage
{
    private const string StorageKey = "contextmemory.admin.settings";

    public async Task<AdminSettings?> LoadAsync(CancellationToken cancellationToken = default)
    {
        var json = await BrowserLocalStorage.GetItemAsync(js, StorageKey, cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
            return DefaultSettings();

        return JsonSerializer.Deserialize<AdminSettings>(json) ?? DefaultSettings();
    }

    public Task SaveAsync(AdminSettings settings, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(settings);
        return BrowserLocalStorage.SetItemAsync(js, StorageKey, json, cancellationToken);
    }

    public Task ClearAsync(CancellationToken cancellationToken = default) =>
        BrowserLocalStorage.RemoveItemAsync(js, StorageKey, cancellationToken);

    private AdminSettings DefaultSettings() => new()
    {
        ApiBaseUrl = configuration["ApiBaseUrl"] ?? "http://localhost:5100",
        MasterKey = string.Empty
    };
}
