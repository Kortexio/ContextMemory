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
}
