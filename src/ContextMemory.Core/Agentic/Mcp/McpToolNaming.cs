namespace ContextMemory.Core.Agentic.Mcp;

public static class McpToolNaming
{
    public const string Separator = "__";

    public static string ToQualifiedName(string serverName, string toolName) =>
        $"{SanitizeForCompare(serverName)}{Separator}{SanitizeForCompare(toolName)}";

    public static bool TryParseQualifiedName(string qualifiedName, out string serverName, out string toolName)
    {
        serverName = string.Empty;
        toolName = string.Empty;

        var idx = qualifiedName.IndexOf(Separator, StringComparison.Ordinal);
        if (idx <= 0 || idx >= qualifiedName.Length - Separator.Length)
            return false;

        serverName = qualifiedName[..idx];
        toolName = qualifiedName[(idx + Separator.Length)..];
        return !string.IsNullOrWhiteSpace(serverName) && !string.IsNullOrWhiteSpace(toolName);
    }

    public static string SanitizeForCompare(string value) =>
        new(value.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());

    /// <summary>
    /// Compares a configured integration name against a server name parsed from a model-issued
    /// tool call. Some models (notably Gemma via Ollama's native function-calling grammar) rewrite
    /// '-' as '_' in generated identifiers; treat them as equivalent here so dispatch still
    /// succeeds instead of silently failing every iteration until the agentic loop times out.
    /// The canonical qualified name exposed to the model (<see cref="ToQualifiedName"/>) is
    /// unaffected — this only relaxes matching on the receiving end.
    /// </summary>
    public static bool ServerNamesMatch(string configuredName, string parsedServerName) =>
        string.Equals(
            NormalizeForFuzzyMatch(configuredName),
            NormalizeForFuzzyMatch(parsedServerName),
            StringComparison.OrdinalIgnoreCase);

    private static string NormalizeForFuzzyMatch(string value) =>
        SanitizeForCompare(value).Replace('_', '-');
}
