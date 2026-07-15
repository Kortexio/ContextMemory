using System.Text.Json;
using ContextMemory.Admin.UI.Models;
using Microsoft.JSInterop;

namespace ContextMemory.Admin.UI.Services;

public sealed class BrowserChatTestSettingsStorage(IJSRuntime js) : IChatTestSettingsStorage
{
    private const string StorageKey = "contextmemory.admin.chat-test";

    public async Task<ChatTestSettings?> LoadAsync(CancellationToken cancellationToken = default)
    {
        var json = await BrowserLocalStorage.GetItemAsync(js, StorageKey, cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
            return null;

        return JsonSerializer.Deserialize<ChatTestSettings>(json);
    }

    public Task SaveAsync(ChatTestSettings settings, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(settings);
        return BrowserLocalStorage.SetItemAsync(js, StorageKey, json, cancellationToken);
    }
}
