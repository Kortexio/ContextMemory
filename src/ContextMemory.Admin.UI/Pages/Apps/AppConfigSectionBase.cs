using ContextMemory.Admin.UI.Components;
using ContextMemory.Admin.UI.Models;
using ContextMemory.Admin.UI.Services;
using ContextMemory.Core.Models;
using Microsoft.AspNetCore.Components;
using static ContextMemory.Admin.UI.Components.AgenticConfigEditor;

namespace ContextMemory.Admin.UI.Pages.Apps;

public abstract class AppConfigSectionBase : ComponentBase
{
    [Parameter] public string AppId { get; set; } = string.Empty;

    [Inject] protected AdminApiClient Api { get; set; } = default!;
    [Inject] protected ChatClient ChatApi { get; set; } = default!;
    [Inject] protected AdminSession Session { get; set; } = default!;

    protected AppRuntimeConfigDto? Loaded { get; private set; }
    protected bool Loading { get; private set; } = true;
    protected bool Saving { get; private set; }
    protected string? Message { get; private set; }
    protected string? Error { get; private set; }
    protected string? LoadError { get; private set; }

    protected override async Task OnParametersSetAsync()
    {
        if (!Session.IsConfigured)
            await Session.InitializeAsync();
        await LoadAsync();
    }

    protected async Task LoadAsync()
    {
        Loading = true;
        LoadError = null;
        Message = null;
        Error = null;

        if (!Session.IsConfigured)
        {
            LoadError = "Configure Settings first.";
            Loading = false;
            return;
        }

        try
        {
            var credentials = await Api.GetAppCredentialsAsync(AppId);
            if (credentials is null || string.IsNullOrWhiteSpace(credentials.ApiKey))
            {
                LoadError = "API key unavailable — could not load config.";
                return;
            }

            var settings = new ChatTestSettings
            {
                AppId = AppId,
                UserId = "admin-config",
                ApiKey = credentials.ApiKey,
                Model = "qwen3.5:9b"
            };

            Loaded = await ChatApi.GetAppConfigAsync(settings);
            if (Loaded is null)
            {
                LoadError = "Empty config.";
                return;
            }

            OnConfigLoaded(Loaded);
        }
        catch (Exception ex)
        {
            LoadError = ex.Message;
        }
        finally
        {
            Loading = false;
        }
    }

    protected abstract void OnConfigLoaded(AppRuntimeConfigDto config);

    protected abstract AppConfigPatchRequest BuildPatch();

    protected async Task SaveAsync()
    {
        Saving = true;
        Message = null;
        Error = null;
        try
        {
            var updated = await Api.PatchConfigAsync(AppId, BuildPatch());
            if (updated is not null)
            {
                Loaded = updated;
                OnConfigLoaded(updated);
            }

            Message = "Saved. Changes apply to new requests immediately.";
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            Saving = false;
        }
    }

    protected static AgenticConfigForm AgenticFrom(AppRuntimeConfigDto? config) =>
        AgenticConfigForm.FromConfig(config?.Agentic);
}
