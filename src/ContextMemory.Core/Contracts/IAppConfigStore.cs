using ContextMemory.Core.Models;

namespace ContextMemory.Core.Contracts;

/// <summary>
/// Reads and writes per-tenant runtime configuration.
/// </summary>
public interface IAppConfigStore
{
    string ProfilesRoot { get; }
    AppRuntimeConfig GetConfig(string appId);
    Task<AppRuntimeConfig> ReloadAsync(string appId, CancellationToken cancellationToken = default);
    Task<AppRuntimeConfig> UpdateAsync(
        string appId,
        AppConfigPatchRequest patch,
        CancellationToken cancellationToken = default);
    void EnsureProfileExists(string appId, AppRuntimeConfig? seed = null);
}
