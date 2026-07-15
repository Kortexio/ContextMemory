using Microsoft.JSInterop;

namespace ContextMemory.Admin.UI.Services;

internal static class BrowserLocalStorage
{
    public static async Task<string?> GetItemAsync(
        IJSRuntime js,
        string key,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await js.InvokeAsync<string?>("localStorage.getItem", cancellationToken, key)
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // Static / server prerender — browser not available yet.
            return null;
        }
        catch (JSException)
        {
            return null;
        }
    }

    public static async Task SetItemAsync(
        IJSRuntime js,
        string key,
        string value,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", cancellationToken, key, value)
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // Ignore during static render.
        }
    }

    public static async Task RemoveItemAsync(
        IJSRuntime js,
        string key,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await js.InvokeVoidAsync("localStorage.removeItem", cancellationToken, key)
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // Ignore during static render.
        }
    }
}
