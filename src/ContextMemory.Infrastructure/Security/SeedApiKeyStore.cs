using System.Text.Json;
using ContextMemory.Core.Configuration;

namespace ContextMemory.Infrastructure.Security;

public static class SeedApiKeyStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static string? TryLoadOverride(ContextMemoryOptions options, string appId)
    {
        var path = GetKeyPath(options, appId);
        if (!File.Exists(path))
            return null;

        try
        {
            var doc = JsonSerializer.Deserialize<SeedApiKeyRecord>(File.ReadAllText(path), JsonOptions);
            return string.IsNullOrWhiteSpace(doc?.ApiKey) ? null : doc.ApiKey;
        }
        catch
        {
            return null;
        }
    }

    public static void SaveOverride(ContextMemoryOptions options, string appId, string apiKey)
    {
        var dir = GetDirectory(options);
        Directory.CreateDirectory(dir);
        var record = new SeedApiKeyRecord
        {
            AppId = appId,
            ApiKey = apiKey,
            RotatedAt = DateTimeOffset.UtcNow
        };
        File.WriteAllText(GetKeyPath(options, appId), JsonSerializer.Serialize(record, JsonOptions));
    }

    private static string GetDirectory(ContextMemoryOptions options) =>
        Path.Combine(Path.GetFullPath(options.DataPath, options.ContentRootPath), "seed-api-keys");

    private static string GetKeyPath(ContextMemoryOptions options, string appId) =>
        Path.Combine(GetDirectory(options), $"{appId}.json");

    private sealed class SeedApiKeyRecord
    {
        public string AppId { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public DateTimeOffset RotatedAt { get; set; }
    }
}
