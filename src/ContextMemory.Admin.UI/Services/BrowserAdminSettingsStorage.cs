using System.Text.Json;
using ContextMemory.Admin.UI.Models;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;

namespace ContextMemory.Admin.UI.Services;

public sealed class BrowserAdminSettingsStorage(
    IJSRuntime js,
    IOptions<AdminUiOptions> options) : IAdminSettingsStorage
{
    private const string StorageKey = "contextmemory.admin.settings";

    public async Task<AdminSettings?> LoadAsync(CancellationToken cancellationToken = default)
    {
        var json = await BrowserLocalStorage.GetItemAsync(js, StorageKey, cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
            return DefaultSettings();

        var stored = JsonSerializer.Deserialize<AdminSettings>(json);
        if (stored is null)
            return DefaultSettings();

        // Fill blanks from host defaults (Docker / first run)
        if (string.IsNullOrWhiteSpace(stored.ApiBaseUrl))
            stored.ApiBaseUrl = DefaultSettings().ApiBaseUrl;
        if (string.IsNullOrWhiteSpace(stored.MasterKey) && !string.IsNullOrWhiteSpace(options.Value.DefaultMasterKey))
            stored.MasterKey = options.Value.DefaultMasterKey;

        return stored;
    }

    public Task SaveAsync(AdminSettings settings, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(settings);
        return BrowserLocalStorage.SetItemAsync(js, StorageKey, json, cancellationToken);
    }

    public Task ClearAsync(CancellationToken cancellationToken = default) =>
        BrowserLocalStorage.RemoveItemAsync(js, StorageKey, cancellationToken);

    private AdminSettings DefaultSettings()
    {
        var o = options.Value;
        return new AdminSettings
        {
            ApiBaseUrl = string.IsNullOrWhiteSpace(o.DefaultApiBaseUrl)
                ? "http://localhost:5100"
                : o.DefaultApiBaseUrl.TrimEnd('/'),
            MasterKey = o.DefaultMasterKey ?? string.Empty
        };
    }
}
