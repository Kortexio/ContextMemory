using ContextMemory.Core.Models;

namespace ContextMemory.Core.Profile;

internal static class WebSearchConfigMerge
{
    public static WebSearchConfig FromFile(WebSearchConfig? config) => config ?? WebSearchConfig.Disabled;

    public static WebSearchConfig ApplyPatch(WebSearchConfig current, WebSearchConfigPatch? patch)
    {
        if (patch is null)
            return current;

        return current with
        {
            Enabled = patch.Enabled ?? current.Enabled,
            Mode = patch.Mode ?? current.Mode,
            Provider = patch.Provider ?? current.Provider,
            MaxResults = patch.MaxResults ?? current.MaxResults,
            MaxContextChars = patch.MaxContextChars ?? current.MaxContextChars,
            PersistToWiki = patch.PersistToWiki ?? current.PersistToWiki,
            LogWebSearch = patch.LogWebSearch ?? current.LogWebSearch
        };
    }
}
