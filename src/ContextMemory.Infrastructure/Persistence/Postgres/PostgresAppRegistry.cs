using System.Collections.Concurrent;
using System.Text.Json;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using ContextMemory.Core.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ContextMemory.Infrastructure.Persistence.Postgres;

public sealed class PostgresAppRegistry : IAppRegistry
{
    private readonly ConcurrentDictionary<string, AppProfile> _apps = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, RegisteredAppRecord> _registrations = new(StringComparer.Ordinal);
    private readonly HashSet<string> _seededAppIds = new(StringComparer.Ordinal);
    private readonly ContextMemoryOptions _config;
    private readonly IDbContextFactory<ContextMemoryDbContext> _dbFactory;
    private readonly object _loadLock = new();
    private volatile bool _registrationsLoaded;

    public PostgresAppRegistry(
        IOptions<ContextMemoryOptions> options,
        IDbContextFactory<ContextMemoryDbContext> dbFactory)
    {
        _config = options.Value;
        _dbFactory = dbFactory;

        foreach (var (appId, entry) in _config.Apps)
        {
            var apiKey = SeedApiKeyStore.TryLoadOverride(_config, appId) ?? entry.ApiKey;
            _apps[appId] = AppRegistryHelper.CreateProfile(appId, apiKey, entry, _config);
            _seededAppIds.Add(appId);
        }
    }

    private void EnsureRegisteredAppsLoaded()
    {
        if (_registrationsLoaded)
            return;

        lock (_loadLock)
        {
            if (_registrationsLoaded)
                return;

            LoadRegisteredApps();
            _registrationsLoaded = true;
        }
    }

    public bool TryGetApp(string appId, out AppProfile? profile)
    {
        EnsureRegisteredAppsLoaded();
        return _apps.TryGetValue(appId, out profile);
    }

    public bool ValidateApiKey(string appId, string apiKey)
    {
        EnsureRegisteredAppsLoaded();
        return _apps.TryGetValue(appId, out var profile)
            && string.Equals(profile.ApiKey, apiKey, StringComparison.Ordinal);
    }

    public IReadOnlyCollection<AppProfile> GetAllApps()
    {
        EnsureRegisteredAppsLoaded();
        return _apps.Values.ToList();
    }

    public bool TryGetRegistration(string appId, out RegisteredAppRecord? record)
    {
        EnsureRegisteredAppsLoaded();
        return _registrations.TryGetValue(appId, out record);
    }

    public string GetAppSource(string appId)
    {
        EnsureRegisteredAppsLoaded();
        return _registrations.ContainsKey(appId) ? "registered" : _seededAppIds.Contains(appId) ? "seed" : "unknown";
    }

    public bool Register(AppProfile profile, RegisteredAppRecord record)
    {
        if (!_apps.TryAdd(profile.AppId, profile))
            return false;

        _registrations[profile.AppId] = record;

        using var db = _dbFactory.CreateDbContext();
        db.RegisteredApps.Add(new RegisteredAppEntity
        {
            AppId = record.AppId,
            ApiKey = record.ApiKey,
            AppName = record.AppName,
            Domain = record.Domain,
            RegisteredAt = record.RegisteredAt,
            IsActive = true
        });
        db.SaveChanges();
        return true;
    }

    public bool TryGetCredentials(string appId, out AppCredentialsInfo? credentials)
    {
        EnsureRegisteredAppsLoaded();
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
        EnsureRegisteredAppsLoaded();
        if (!_apps.TryGetValue(appId, out var profile) || profile is null)
        {
            credentials = null;
            return false;
        }

        var newKey = ApiKeyGenerator.CreateLiveKey();
        _apps[appId] = profile with { ApiKey = newKey };

        var source = GetAppSource(appId);
        if (_registrations.ContainsKey(appId))
        {
            var record = _registrations[appId] with { ApiKey = newKey };
            _registrations[appId] = record;

            using var db = _dbFactory.CreateDbContext();
            var entity = db.RegisteredApps.FirstOrDefault(x => x.AppId == appId);
            if (entity is not null)
            {
                entity.ApiKey = newKey;
                db.SaveChanges();
            }
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
        EnsureRegisteredAppsLoaded();
        if (!_apps.TryGetValue(appId, out var profile) || profile is null)
            return false;

        if (!profile.IsActive)
            return true;

        _apps[appId] = profile with { IsActive = false };

        if (_registrations.ContainsKey(appId))
        {
            var record = _registrations[appId] with { IsActive = false };
            _registrations[appId] = record;

            using var db = _dbFactory.CreateDbContext();
            var entity = db.RegisteredApps.FirstOrDefault(x => x.AppId == appId);
            if (entity is not null)
            {
                entity.IsActive = false;
                db.SaveChanges();
            }
        }

        return true;
    }

    private void LoadRegisteredApps()
    {
        using var db = _dbFactory.CreateDbContext();
        foreach (var row in db.RegisteredApps.AsNoTracking())
        {
            var record = new RegisteredAppRecord
            {
                AppId = row.AppId,
                ApiKey = row.ApiKey,
                AppName = row.AppName,
                Domain = row.Domain,
                RegisteredAt = row.RegisteredAt,
                IsActive = row.IsActive
            };

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
    }
}
