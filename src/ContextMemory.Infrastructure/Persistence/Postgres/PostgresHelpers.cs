using System.Text.Json;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Models;

namespace ContextMemory.Infrastructure.Persistence.Postgres;

internal static class PostgresJson
{
    public static readonly JsonSerializerOptions CamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
}

internal static class AppRegistryHelper
{
    public static AppProfile CreateProfile(string appId, string apiKey, AppOptionsEntry entry, ContextMemoryOptions config) =>
        new()
        {
            AppId = appId,
            ApiKey = apiKey,
            SystemPrompt = entry.SystemPrompt,
            DefaultLanguage = entry.DefaultLanguage,
            MaxHistoryMessages = entry.MaxHistoryMessages > 0
                ? entry.MaxHistoryMessages
                : config.MaxHistoryMessages
        };
}
