namespace ContextMemory.Core.Agentic.Mcp;

/// <summary>
/// CodeForge-style unknown/phantom MCP name handling: remap literal <c>server__*</c>
/// placeholders and wrong-server short names when unambiguous; otherwise block.
/// </summary>
public static class McpPhantomToolResolver
{
    /// <summary>
    /// Returns the catalog tool name to execute, or <c>null</c> when the name must be rejected
    /// (unknown / invented). Never returns a non-catalog qualified name.
    /// </summary>
    public static string? Resolve(string? name, IReadOnlyCollection<string> catalogNames)
    {
        if (string.IsNullOrWhiteSpace(name) || catalogNames.Count == 0)
            return null;

        var trimmed = name.Trim();
        var exact = catalogNames.FirstOrDefault(n =>
            string.Equals(n, trimmed, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
            return exact;

        var expanded = ClientSideToolCalling.TryExpandShortMcpName(trimmed, catalogNames);
        if (expanded is not null)
            return expanded;

        if (!McpToolNaming.TryParseQualifiedName(trimmed, out var server, out var shortName))
            return null;

        // Literal placeholder copied from nudges: server__query_objects → unique short match.
        var placeholderServer = string.Equals(server, "server", StringComparison.OrdinalIgnoreCase);

        string? match = null;
        foreach (var candidate in catalogNames)
        {
            if (!McpToolNaming.TryParseQualifiedName(candidate, out var candServer, out var candShort))
                continue;
            if (!string.Equals(candShort, shortName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!placeholderServer && McpToolNaming.ServerNamesMatch(candServer, server))
                return candidate; // same server, exact short name (sanitized compare)

            if (match is not null)
                return null; // ambiguous short name across servers
            match = candidate;
        }

        // Wrong/unknown server but unique short name in catalog → remap (phantom server prefix).
        if (match is not null && (placeholderServer || !CatalogHasServer(catalogNames, server)))
            return match;

        return null;
    }

    public static bool IsLiteralServerPlaceholder(string? name) =>
        McpToolNaming.TryParseQualifiedName(name ?? string.Empty, out var server, out _)
        && string.Equals(server, "server", StringComparison.OrdinalIgnoreCase);

    private static bool CatalogHasServer(IReadOnlyCollection<string> catalogNames, string server)
    {
        foreach (var candidate in catalogNames)
        {
            if (McpToolNaming.TryParseQualifiedName(candidate, out var candServer, out _)
                && McpToolNaming.ServerNamesMatch(candServer, server))
                return true;
        }

        return false;
    }
}
