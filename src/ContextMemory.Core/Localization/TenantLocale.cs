namespace ContextMemory.Core.Localization;

/// <summary>
/// Resolves tenant-facing strings from <c>DefaultLanguage</c> (BCP-47).
/// English is the default; Portuguese (pt-*) selects alternate copy where defined.
/// </summary>
public static class TenantLocale
{
    public static bool IsPortuguese(string? language) =>
        !string.IsNullOrWhiteSpace(language)
        && language.StartsWith("pt", StringComparison.OrdinalIgnoreCase);

    public static string Select(string? language, string english, string portuguese) =>
        IsPortuguese(language) ? portuguese : english;
}
