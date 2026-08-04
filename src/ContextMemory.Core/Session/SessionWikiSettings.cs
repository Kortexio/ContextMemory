using ContextMemory.Core.Models;

namespace ContextMemory.Core.Session;

public static class SessionWikiSettings
{
    public static int ResolveMaxWikiContextChars(AppRuntimeConfig config, Configuration.ContextMemoryOptions defaults) =>
        config.MaxWikiContextChars > 0 ? config.MaxWikiContextChars : defaults.MaxWikiContextChars;

    public static long ResolveCompactionThresholdBytes(AppRuntimeConfig config, Configuration.ContextMemoryOptions defaults) =>
        config.WikiCompactionThresholdBytes > 0
            ? config.WikiCompactionThresholdBytes
            : defaults.WikiCompactionThresholdBytes;

    public static int ResolveCompactionMinPages(AppRuntimeConfig config, Configuration.ContextMemoryOptions defaults) =>
        config.WikiCompactionMinPages > 0 ? config.WikiCompactionMinPages : defaults.WikiCompactionMinPages;

    public static bool ShouldCompact(SessionSnapshot snapshot, AppRuntimeConfig config, Configuration.ContextMemoryOptions defaults)
    {
        var minPages = ResolveCompactionMinPages(config, defaults);
        var pageCount = SessionWikiHelpers.CountWikiPages(snapshot.SessionPath);
        if (pageCount < minPages)
            return false;

        var threshold = ResolveCompactionThresholdBytes(config, defaults);
        return SessionWikiHelpers.GetDirectorySizeBytes(snapshot.SessionPath) > threshold;
    }

    public static int ResolveMaintainerWikiBudgetChars(AppRuntimeConfig config, Configuration.ContextMemoryOptions defaults) =>
        Math.Min(ResolveMaxWikiContextChars(config, defaults) * 2, 24_000);

    /// <summary>App setting; values ≤0 are treated as 1 (every turn).</summary>
    public static int ResolveWikiUpdateEveryNTurns(AppRuntimeConfig config) =>
        config.WikiUpdateEveryNTurns <= 0 ? 1 : config.WikiUpdateEveryNTurns;

    /// <summary>
    /// Counts assistant messages in the session (after append) and returns true when turns % N == 0.
    /// </summary>
    public static bool ShouldRunWikiLlm(SessionSnapshot snapshot, int everyNTurns)
    {
        var n = everyNTurns <= 0 ? 1 : everyNTurns;
        var turns = 0;
        foreach (var message in snapshot.Messages)
        {
            if (string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase))
                turns++;
        }

        return turns > 0 && turns % n == 0;
    }

    /// <summary>
    /// Strict order: app WikiLlmModel → platform default → app LlmModel.
    /// </summary>
    public static string ResolveWikiLlmModel(AppRuntimeConfig appConfig, string? platformDefaultWikiModel)
    {
        if (!string.IsNullOrWhiteSpace(appConfig.WikiLlmModel))
            return appConfig.WikiLlmModel.Trim();

        if (!string.IsNullOrWhiteSpace(platformDefaultWikiModel))
            return platformDefaultWikiModel.Trim();

        return appConfig.LlmModel;
    }
}
