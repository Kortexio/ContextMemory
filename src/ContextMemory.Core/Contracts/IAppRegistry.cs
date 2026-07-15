using ContextMemory.Core.Models;

namespace ContextMemory.Core.Contracts;

/// <summary>
/// Tenant registry: app metadata, API keys, and activation state.
/// </summary>
public interface IAppRegistry
{
    bool TryGetApp(string appId, out AppProfile? profile);
    bool ValidateApiKey(string appId, string apiKey);
    IReadOnlyCollection<AppProfile> GetAllApps();
    bool TryGetRegistration(string appId, out RegisteredAppRecord? record);
    string GetAppSource(string appId);
    bool Register(AppProfile profile, RegisteredAppRecord record);
    bool TryGetCredentials(string appId, out AppCredentialsInfo? credentials);
    bool TryRotateApiKey(string appId, out AppCredentialsInfo? credentials);
    bool TryDeactivateApp(string appId);
}
