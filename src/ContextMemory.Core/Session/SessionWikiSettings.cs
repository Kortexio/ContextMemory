namespace ContextMemory.Core.Session;

public static class SessionWikiSettings
{
    public static int ResolveMaxWikiContextChars(Models.AppRuntimeConfig config, Configuration.ContextMemoryOptions defaults) =>
        config.MaxWikiContextChars > 0 ? config.MaxWikiContextChars : defaults.MaxWikiContextChars;

    public static long ResolveCompactionThresholdBytes(Models.AppRuntimeConfig config, Configuration.ContextMemoryOptions defaults) =>
        config.WikiCompactionThresholdBytes > 0
            ? config.WikiCompactionThresholdBytes
            : defaults.WikiCompactionThresholdBytes;

    public static int ResolveCompactionMinPages(Models.AppRuntimeConfig config, Configuration.ContextMemoryOptions defaults) =>
        config.WikiCompactionMinPages > 0 ? config.WikiCompactionMinPages : defaults.WikiCompactionMinPages;

    public static bool ShouldCompact(SessionSnapshot snapshot, Models.AppRuntimeConfig config, Configuration.ContextMemoryOptions defaults)
    {
        var minPages = ResolveCompactionMinPages(config, defaults);
        var pageCount = SessionWikiHelpers.CountWikiPages(snapshot.SessionPath);
        if (pageCount < minPages)
            return false;

        var threshold = ResolveCompactionThresholdBytes(config, defaults);
        return SessionWikiHelpers.GetDirectorySizeBytes(snapshot.SessionPath) > threshold;
    }

    public static int ResolveMaintainerWikiBudgetChars(Models.AppRuntimeConfig config, Configuration.ContextMemoryOptions defaults) =>
        Math.Min(ResolveMaxWikiContextChars(config, defaults) * 2, 24_000);
}
