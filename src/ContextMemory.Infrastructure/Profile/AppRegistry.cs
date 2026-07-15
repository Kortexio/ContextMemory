using System.Collections.Concurrent;
using System.Text.Json;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using ContextMemory.Core.Security;
using Microsoft.Extensions.Options;

namespace ContextMemory.Infrastructure.Profile;

public sealed class AppRegistry : IAppRegistry
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly ConcurrentDictionary<string, AppProfile> _apps = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, RegisteredAppRecord> _registrations = new(StringComparer.Ordinal);
    private readonly HashSet<string> _seededAppIds = new(StringComparer.Ordinal);
    private readonly ContextMemoryOptions _config;
    private readonly string _registeredAppsPath;

    public AppRegistry(IOptions<ContextMemoryOptions> options)
    {
        _config = options.Value;
        _registeredAppsPath = Path.Combine(
            Path.GetFullPath(_config.DataPath, _config.ContentRootPath),
            "registered-apps");
        Directory.CreateDirectory(_registeredAppsPath);

        foreach (var (appId, entry) in _config.Apps)
        {
            var apiKey = SeedApiKeyStore.TryLoadOverride(_config, appId) ?? entry.ApiKey;
            _apps[appId] = CreateProfile(appId, apiKey, entry);
            _seededAppIds.Add(appId);
        }

        LoadRegisteredApps();
    }

    public bool TryGetApp(string appId, out AppProfile? profile) =>
        _apps.TryGetValue(appId, out profile);

    public bool ValidateApiKey(string appId, string apiKey) =>
        _apps.TryGetValue(appId, out var profile)
        && string.Equals(profile.ApiKey, apiKey, StringComparison.Ordinal);

    public IReadOnlyCollection<AppProfile> GetAllApps() => _apps.Values.ToList();

    public bool TryGetRegistration(string appId, out RegisteredAppRecord? record) =>
        _registrations.TryGetValue(appId, out record);

    public string GetAppSource(string appId) =>
        _registrations.ContainsKey(appId) ? "registered" : _seededAppIds.Contains(appId) ? "seed" : "unknown";

    public bool Register(AppProfile profile, RegisteredAppRecord record)
    {
        if (!_apps.TryAdd(profile.AppId, profile))
            return false;

        _registrations[profile.AppId] = record;
        var path = Path.Combine(_registeredAppsPath, $"{profile.AppId}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(record, JsonOptions));
        return true;
    }

    public bool TryGetCredentials(string appId, out AppCredentialsInfo? credentials)
    {
        if (!_apps.TryGetValue(appId, out var profile) || profile is null)
        {
            credentials = null;
            return false;
        }

        var source = GetAppSource(appId);
        var rotationPersists = source == "registered"
            || (_seededAppIds.Contains(appId) && SeedApiKeyStore.TryLoadOverride(_config, appId) is not null);
        credentials = new AppCredentialsInfo
        {
            AppId = appId,
            ApiKey = profile.ApiKey,
            Source = source,
            RotationPersists = rotationPersists
        };
        return true;
    }

    public bool TryRotateApiKey(string appId, out AppCredentialsInfo? credentials)
    {
        if (!_apps.TryGetValue(appId, out var profile) || profile is null)
        {
            credentials = null;
            return false;
        }

        var newKey = ApiKeyGenerator.CreateLiveKey();
        _apps[appId] = profile with { ApiKey = newKey };

        var source = GetAppSource(appId);
        if (_registrations.TryGetValue(appId, out var record))
        {
            var updatedRecord = record with { ApiKey = newKey };
            _registrations[appId] = updatedRecord;
            var path = Path.Combine(_registeredAppsPath, $"{appId}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(updatedRecord, JsonOptions));
        }
        else if (_seededAppIds.Contains(appId))
        {
            SeedApiKeyStore.SaveOverride(_config, appId, newKey);
        }

        credentials = new AppCredentialsInfo
        {
            AppId = appId,
            ApiKey = newKey,
            Source = source,
            RotationPersists = source == "registered" || _seededAppIds.Contains(appId)
        };
        return true;
    }

    public bool TryDeactivateApp(string appId)
    {
        if (!_apps.TryGetValue(appId, out var profile) || profile is null)
            return false;

        if (!profile.IsActive)
            return true;

        _apps[appId] = profile with { IsActive = false };

        if (_registrations.TryGetValue(appId, out var record))
        {
            var updatedRecord = record with { IsActive = false };
            _registrations[appId] = updatedRecord;
            var path = Path.Combine(_registeredAppsPath, $"{appId}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(updatedRecord, JsonOptions));
        }

        return true;
    }

    internal string RegisteredAppsDirectory => _registeredAppsPath;

    private void LoadRegisteredApps()
    {
        if (!Directory.Exists(_registeredAppsPath))
            return;

        foreach (var file in Directory.EnumerateFiles(_registeredAppsPath, "*.json"))
        {
            try
            {
                var record = JsonSerializer.Deserialize<RegisteredAppRecord>(File.ReadAllText(file), JsonOptions);
                if (record is null)
                    continue;

                var profile = new AppProfile
                {
                    AppId = record.AppId,
                    ApiKey = record.ApiKey,
                    DefaultLanguage = "en-US",
                    IsActive = record.IsActive
                };

                _apps[record.AppId] = profile;
                _registrations[record.AppId] = record;
            }
            catch
            {
                // Skip corrupt registration files
            }
        }
    }

    private AppProfile CreateProfile(string appId, string apiKey, AppOptionsEntry entry) =>
        new()
        {
            AppId = appId,
            ApiKey = apiKey,
            SystemPrompt = entry.SystemPrompt,
            DefaultLanguage = entry.DefaultLanguage,
            MaxHistoryMessages = entry.MaxHistoryMessages > 0
                ? entry.MaxHistoryMessages
                : _config.MaxHistoryMessages
        };
}
