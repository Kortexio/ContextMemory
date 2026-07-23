using System.Collections.Concurrent;
using System.Text.Json;
using ContextMemory.Infrastructure.Agentic;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using ContextMemory.Infrastructure.Profile;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ContextMemory.Infrastructure.Persistence.Postgres;

public sealed class PostgresAppConfigStore : IAppConfigStore
{
    private readonly ContextMemoryOptions _defaults;
    private readonly IDbContextFactory<ContextMemoryDbContext> _dbFactory;
    private readonly ConcurrentDictionary<string, AppRuntimeConfig> _cache = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private readonly ILogger<PostgresAppConfigStore> _logger;

    public PostgresAppConfigStore(
        IOptions<ContextMemoryOptions> options,
        IDbContextFactory<ContextMemoryDbContext> dbFactory,
        ILogger<PostgresAppConfigStore> logger)
    {
        _defaults = options.Value;
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public string ProfilesRoot =>
        Path.Combine(Path.GetFullPath(_defaults.DataPath, _defaults.ContentRootPath), "app-profiles");

    public AppRuntimeConfig GetConfig(string appId) =>
        _cache.GetOrAdd(appId, _ => LoadConfig(appId));

    public async Task<AppRuntimeConfig> ReloadAsync(string appId, CancellationToken cancellationToken = default)
    {
        var gate = GetLock(appId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var config = LoadConfig(appId);
            _cache[appId] = config;
            return config;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<AppRuntimeConfig> UpdateAsync(
        string appId,
        AppConfigPatchRequest patch,
        CancellationToken cancellationToken = default)
    {
        var gate = GetLock(appId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var row = await GetOrCreateRowAsync(db, appId, cancellationToken).ConfigureAwait(false);

            if (patch.BasePersona is not null)
                row.Persona = patch.BasePersona;
            if (patch.BusinessRules is not null)
                row.BusinessRules = patch.BusinessRules;
            if (patch.FormatRules is not null)
                row.FormatRules = patch.FormatRules;
            if (patch.WikiSchema is not null)
                row.WikiSchema = patch.WikiSchema;

            var current = JsonSerializer.Deserialize<AppConfigFile>(row.ConfigJson, PostgresJson.CamelCase)
                          ?? new AppConfigFile();

            var updated = current with
            {
                DefaultLanguage = patch.DefaultLanguage ?? current.DefaultLanguage,
                LlmModel = patch.LlmModel ?? current.LlmModel,
                LlmBackend = patch.LlmBackend ?? current.LlmBackend,
                MaxHistoryMessages = patch.MaxHistoryMessages ?? current.MaxHistoryMessages,
                MaxWikiContextChars = patch.MaxWikiContextChars ?? current.MaxWikiContextChars,
                WikiCompactionThresholdBytes = patch.WikiCompactionThresholdBytes ?? current.WikiCompactionThresholdBytes,
                WikiCompactionMinPages = patch.WikiCompactionMinPages ?? current.WikiCompactionMinPages,
                StreamingEnabled = patch.StreamingEnabled ?? current.StreamingEnabled,
                WebSearch = WebSearchConfigMerge.ApplyPatch(
                    WebSearchConfigMerge.FromFile(current.WebSearch),
                    patch.WebSearch),
                RateLimits = patch.RateLimits ?? current.RateLimits,
                Agentic = patch.Agentic ?? current.Agentic,
                GlobalWikiEnabled = patch.GlobalWikiEnabled ?? current.GlobalWikiEnabled,
                MaxGlobalWikiToolChars = patch.MaxGlobalWikiToolChars ?? current.MaxGlobalWikiToolChars
            };

            row.ConfigJson = JsonSerializer.Serialize(updated, PostgresJson.CamelCase);
            row.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var runtime = LoadConfig(appId);
            _cache[appId] = runtime;
            _logger.LogInformation("App config updated for {AppId} (Postgres)", appId);
            return runtime;
        }
        finally
        {
            gate.Release();
        }
    }

    public void EnsureProfileExists(string appId, AppRuntimeConfig? seed = null)
    {
        using var db = _dbFactory.CreateDbContext();
        var row = db.AppProfiles.FirstOrDefault(x => x.AppId == appId);
        if (row is not null && !string.IsNullOrWhiteSpace(row.ConfigJson) && row.ConfigJson != "{}")
        {
            _cache[appId] = LoadConfig(appId);
            return;
        }

        seed ??= new AppRuntimeConfig { AppId = appId };
        var configFile = new AppConfigFile
        {
            DefaultLanguage = seed.DefaultLanguage,
            LlmModel = seed.LlmModel,
            LlmBackend = seed.LlmBackend,
            MaxHistoryMessages = seed.MaxHistoryMessages,
            MaxWikiContextChars = seed.MaxWikiContextChars > 0 ? seed.MaxWikiContextChars : _defaults.MaxWikiContextChars,
            WikiCompactionThresholdBytes = seed.WikiCompactionThresholdBytes > 0
                ? seed.WikiCompactionThresholdBytes
                : _defaults.WikiCompactionThresholdBytes,
            WikiCompactionMinPages = seed.WikiCompactionMinPages > 0
                ? seed.WikiCompactionMinPages
                : _defaults.WikiCompactionMinPages,
            StreamingEnabled = seed.StreamingEnabled,
            Agentic = seed.Agentic,
            GlobalWikiEnabled = seed.GlobalWikiEnabled,
            MaxGlobalWikiToolChars = seed.MaxGlobalWikiToolChars
        };

        if (row is null)
        {
            db.AppProfiles.Add(new AppProfileEntity
            {
                AppId = appId,
                Persona = seed.BasePersona,
                BusinessRules = seed.BusinessRules,
                FormatRules = seed.FormatRules,
                ConfigJson = JsonSerializer.Serialize(configFile, PostgresJson.CamelCase),
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            row.Persona = seed.BasePersona;
            row.BusinessRules = seed.BusinessRules;
            row.FormatRules = seed.FormatRules;
            row.ConfigJson = JsonSerializer.Serialize(configFile, PostgresJson.CamelCase);
            row.UpdatedAt = DateTimeOffset.UtcNow;
        }

        db.SaveChanges();
        _cache[appId] = LoadConfig(appId);
        _logger.LogInformation("App profile created for {AppId} (Postgres)", appId);
    }

    private AppRuntimeConfig LoadConfig(string appId)
    {
        using var db = _dbFactory.CreateDbContext();
        var row = db.AppProfiles.AsNoTracking().FirstOrDefault(x => x.AppId == appId);
        if (row is null)
        {
            return new AppRuntimeConfig
            {
                AppId = appId,
                LlmModel = _defaults.DefaultLlmModel,
                MaxHistoryMessages = _defaults.MaxHistoryMessages,
                MaxWikiContextChars = _defaults.MaxWikiContextChars,
                WikiCompactionThresholdBytes = _defaults.WikiCompactionThresholdBytes,
                WikiCompactionMinPages = _defaults.WikiCompactionMinPages
            };
        }

        var configFile = JsonSerializer.Deserialize<AppConfigFile>(row.ConfigJson, PostgresJson.CamelCase)
                         ?? new AppConfigFile();

        return new AppRuntimeConfig
        {
            AppId = appId,
            BasePersona = row.Persona.Trim(),
            BusinessRules = row.BusinessRules.Trim(),
            FormatRules = row.FormatRules.Trim(),
            WikiSchema = row.WikiSchema.Trim(),
            DefaultLanguage = configFile.DefaultLanguage,
            LlmModel = string.IsNullOrWhiteSpace(configFile.LlmModel) ? _defaults.DefaultLlmModel : configFile.LlmModel,
            LlmBackend = configFile.LlmBackend,
            MaxHistoryMessages = configFile.MaxHistoryMessages > 0
                ? configFile.MaxHistoryMessages
                : _defaults.MaxHistoryMessages,
            MaxWikiContextChars = configFile.MaxWikiContextChars > 0
                ? configFile.MaxWikiContextChars
                : _defaults.MaxWikiContextChars,
            WikiCompactionThresholdBytes = configFile.WikiCompactionThresholdBytes > 0
                ? configFile.WikiCompactionThresholdBytes
                : _defaults.WikiCompactionThresholdBytes,
            WikiCompactionMinPages = configFile.WikiCompactionMinPages > 0
                ? configFile.WikiCompactionMinPages
                : _defaults.WikiCompactionMinPages,
            StreamingEnabled = configFile.StreamingEnabled,
            RateLimits = configFile.RateLimits ?? new RateLimitConfig
            {
                RequestsPerMinute = _defaults.DefaultRateLimitRpm,
                TokensPerMinute = _defaults.DefaultRateLimitTpm
            },
            WebSearch = WebSearchConfigMerge.FromFile(configFile.WebSearch),
            Agentic = configFile.Agentic ?? AgenticConfig.Disabled,
            GlobalWikiEnabled = configFile.GlobalWikiEnabled ?? true,
            MaxGlobalWikiToolChars = configFile.MaxGlobalWikiToolChars
        };
    }

    private static async Task<AppProfileEntity> GetOrCreateRowAsync(
        ContextMemoryDbContext db,
        string appId,
        CancellationToken cancellationToken)
    {
        var row = await db.AppProfiles.FirstOrDefaultAsync(x => x.AppId == appId, cancellationToken)
            .ConfigureAwait(false);

        if (row is not null)
            return row;

        row = new AppProfileEntity
        {
            AppId = appId,
            ConfigJson = "{}",
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.AppProfiles.Add(row);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return row;
    }

    private SemaphoreSlim GetLock(string appId) =>
        _locks.GetOrAdd(appId, _ => new SemaphoreSlim(1, 1));
}
