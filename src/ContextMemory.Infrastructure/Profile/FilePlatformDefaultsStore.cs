using System.Text.Json;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ContextMemory.Infrastructure.Profile;

public sealed class FilePlatformDefaultsStore : IPlatformDefaultsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly ContextMemoryOptions _options;
    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ILogger<FilePlatformDefaultsStore> _logger;
    private PlatformDefaults? _cachedFile;

    public FilePlatformDefaultsStore(
        IOptions<ContextMemoryOptions> options,
        ILogger<FilePlatformDefaultsStore> logger)
    {
        _options = options.Value;
        var dataRoot = Path.GetFullPath(_options.DataPath, _options.ContentRootPath);
        Directory.CreateDirectory(dataRoot);
        _filePath = Path.Combine(dataRoot, "platform-defaults.json");
        _logger = logger;
    }

    public PlatformDefaults Get() => Merge(ReadFile());

    public async Task<PlatformDefaults> UpdateAsync(
        PlatformDefaultsPatchRequest patch,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = ReadFile();
            var updated = current with
            {
                DefaultWikiLlmModel = patch.DefaultWikiLlmModel ?? current.DefaultWikiLlmModel
            };

            await File.WriteAllTextAsync(
                    _filePath,
                    JsonSerializer.Serialize(updated, JsonOptions),
                    cancellationToken)
                .ConfigureAwait(false);

            _cachedFile = updated;
            _logger.LogInformation("Platform defaults updated");
            return Merge(updated);
        }
        finally
        {
            _gate.Release();
        }
    }

    private PlatformDefaults Merge(PlatformDefaults file)
    {
        var wikiModel = !string.IsNullOrWhiteSpace(file.DefaultWikiLlmModel)
            ? file.DefaultWikiLlmModel.Trim()
            : (_options.DefaultWikiLlmModel ?? string.Empty).Trim();

        return new PlatformDefaults { DefaultWikiLlmModel = wikiModel };
    }

    private PlatformDefaults ReadFile()
    {
        if (_cachedFile is not null)
            return _cachedFile;

        if (!File.Exists(_filePath))
            return new PlatformDefaults();

        try
        {
            var json = File.ReadAllText(_filePath);
            _cachedFile = JsonSerializer.Deserialize<PlatformDefaults>(json, JsonOptions) ?? new PlatformDefaults();
            return _cachedFile;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read platform defaults from {Path}", _filePath);
            return new PlatformDefaults();
        }
    }
}
