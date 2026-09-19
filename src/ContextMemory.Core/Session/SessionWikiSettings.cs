using ContextMemory.Core.Models;

namespace ContextMemory.Core.Session;

public static class SessionWikiSettings
{
    public static int ResolveMaxWikiContextChars(AppRuntimeConfig config, Configuration.ContextMemoryOptions defaults) =>
        config.MaxWikiContextChars > 0 ? config.MaxWikiContextChars : defaults.MaxWikiContextChars;

    public static int ResolveMaxDigestContextChars(AppRuntimeConfig config, Configuration.ContextMemoryOptions defaults) =>
        config.MaxDigestContextChars > 0 ? config.MaxDigestContextChars : defaults.MaxDigestContextChars;

    public static int ResolveDigestTopK(AppRuntimeConfig config, Configuration.ContextMemoryOptions defaults) =>
        config.DigestTopK > 0 ? config.DigestTopK : Math.Max(1, defaults.DigestTopK);

    public static int ResolveMaxToolObservationChars(AppRuntimeConfig config, Configuration.ContextMemoryOptions defaults) =>
        config.MaxToolObservationChars > 0 ? config.MaxToolObservationChars : defaults.MaxToolObservationChars;

    public static int ResolveMaxContextTokens(AppRuntimeConfig config, Configuration.ContextMemoryOptions defaults) =>
        config.MaxContextTokens > 0 ? config.MaxContextTokens : defaults.MaxContextTokens;

    /// <summary>
    /// Mid-turn compaction budget. Caps at ~75% of the model <c>num_ctx</c> when set so
    /// agentic prompts (wiki + tools + history) fit the actual Ollama window (often 4096).
    /// </summary>
    public static int ResolveAgentCompactionTokenBudget(
        AppRuntimeConfig config,
        Configuration.ContextMemoryOptions defaults,
        int? requestNumCtx)
    {
        var wikiBudget = ResolveMaxContextTokens(config, defaults);
        var numCtx = requestNumCtx is > 0
            ? requestNumCtx
            : config.LlmOptions?.NumCtx;
        if (numCtx is not > 0)
            return wikiBudget;

        return Math.Min(wikiBudget, FitBudgetForContextWindow(numCtx.Value));
    }

    /// <summary>Leave headroom for generation and tokenizer drift versus <see cref="Utilities.TokenEstimator"/>.</summary>
    public static int FitBudgetForContextWindow(int nCtx) =>
        Math.Max(512, nCtx * 3 / 4);

    public static int ResolveMaxHistoryMessages(AppRuntimeConfig config, Configuration.ContextMemoryOptions defaults) =>
        config.MaxHistoryMessages > 0 ? config.MaxHistoryMessages : defaults.MaxHistoryMessages;

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
