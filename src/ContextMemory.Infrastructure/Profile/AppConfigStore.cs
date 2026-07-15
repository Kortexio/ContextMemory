using System.Collections.Concurrent;
using System.Text.Json;
using ContextMemory.Infrastructure.Agentic;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ContextMemory.Infrastructure.Profile;

public sealed class AppConfigStore : IAppConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _profilesRoot;

    public string ProfilesRoot => _profilesRoot;
    private readonly ContextMemoryOptions _defaults;
    private readonly ConcurrentDictionary<string, AppRuntimeConfig> _cache = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private readonly ILogger<AppConfigStore> _logger;

    public AppConfigStore(IOptions<ContextMemoryOptions> options, ILogger<AppConfigStore> logger)
    {
        _defaults = options.Value;
        _profilesRoot = Path.Combine(
            Path.GetFullPath(_defaults.DataPath, _defaults.ContentRootPath),
            "app-profiles");
        Directory.CreateDirectory(_profilesRoot);
        _logger = logger;
    }

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
            var dir = GetProfileDir(appId);
            Directory.CreateDirectory(dir);

            if (patch.BasePersona is not null)
                await File.WriteAllTextAsync(Path.Combine(dir, "persona.md"), patch.BasePersona, cancellationToken)
                    .ConfigureAwait(false);

            if (patch.BusinessRules is not null)
                await File.WriteAllTextAsync(Path.Combine(dir, "business-rules.md"), patch.BusinessRules, cancellationToken)
                    .ConfigureAwait(false);

            if (patch.FormatRules is not null)
                await File.WriteAllTextAsync(Path.Combine(dir, "format-rules.md"), patch.FormatRules, cancellationToken)
                    .ConfigureAwait(false);

            if (patch.WikiSchema is not null)
                await File.WriteAllTextAsync(Path.Combine(dir, "wiki-schema.md"), patch.WikiSchema, cancellationToken)
                    .ConfigureAwait(false);

            var configPath = Path.Combine(dir, "config.json");
            var current = File.Exists(configPath)
                ? JsonSerializer.Deserialize<AppConfigFile>(await File.ReadAllTextAsync(configPath, cancellationToken).ConfigureAwait(false), JsonOptions)
                  ?? new AppConfigFile()
                : new AppConfigFile();

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
                Agentic = patch.Agentic ?? current.Agentic
            };

            await File.WriteAllTextAsync(
                configPath,
                JsonSerializer.Serialize(updated, JsonOptions),
                cancellationToken).ConfigureAwait(false);

            var runtime = LoadConfig(appId);
            _cache[appId] = runtime;
            _logger.LogInformation("App config updated for {AppId}", appId);
            return runtime;
        }
        finally
        {
            gate.Release();
        }
    }

    public void EnsureProfileExists(string appId, AppRuntimeConfig? seed = null)
    {
        var dir = GetProfileDir(appId);
        if (Directory.Exists(dir) && File.Exists(Path.Combine(dir, "config.json")))
            return;

        Directory.CreateDirectory(dir);
        seed ??= new AppRuntimeConfig { AppId = appId };

        File.WriteAllText(Path.Combine(dir, "persona.md"), seed.BasePersona);
        File.WriteAllText(Path.Combine(dir, "business-rules.md"), seed.BusinessRules);
        File.WriteAllText(Path.Combine(dir, "format-rules.md"), seed.FormatRules);

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
            WebSearch = seed.WebSearch,
            Agentic = seed.Agentic
        };

        File.WriteAllText(Path.Combine(dir, "config.json"), JsonSerializer.Serialize(configFile, JsonOptions));
        _cache[appId] = LoadConfig(appId);
        _logger.LogInformation("App profile created for {AppId}", appId);
    }

    private AppRuntimeConfig LoadConfig(string appId)
    {
        var dir = GetProfileDir(appId);
        if (!Directory.Exists(dir))
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

        var configFile = ReadConfigFile(dir);
        return new AppRuntimeConfig
        {
            AppId = appId,
            BasePersona = ReadMarkdownFile(dir, "persona.md"),
            BusinessRules = ReadMarkdownFile(dir, "business-rules.md"),
            FormatRules = ReadMarkdownFile(dir, "format-rules.md"),
            WikiSchema = ReadMarkdownFile(dir, "wiki-schema.md"),
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
            Agentic = configFile.Agentic ?? AgenticConfig.Disabled
        };
    }

    private static AppConfigFile ReadConfigFile(string dir)
    {
        var path = Path.Combine(dir, "config.json");
        if (!File.Exists(path))
            return new AppConfigFile();

        return JsonSerializer.Deserialize<AppConfigFile>(File.ReadAllText(path), JsonOptions)
               ?? new AppConfigFile();
    }

    private static string ReadMarkdownFile(string dir, string fileName)
    {
        var path = Path.Combine(dir, fileName);
        return File.Exists(path) ? File.ReadAllText(path).Trim() : string.Empty;
    }

    private string GetProfileDir(string appId) => Path.Combine(_profilesRoot, appId);

    private SemaphoreSlim GetLock(string appId) =>
        _locks.GetOrAdd(appId, _ => new SemaphoreSlim(1, 1));
}
