namespace ContextMemory.Infrastructure.Agentic.Mcp;

/// <summary>
/// Rewrites common Cursor/Windows MCP command specs into Linux container paths for the mcp-runtime sidecar.
/// </summary>
public static class McpStdioPathNormalizer
{
    public static (string Command, List<string> Args, string? Cwd) NormalizeForLinuxContainer(
        string command,
        IReadOnlyList<string>? args,
        string? cwd)
    {
        var normalizedArgs = (args ?? []).Select(NormalizePathSeparators).ToList();
        var normalizedCommand = NormalizeCommand(command);
        var normalizedCwd = string.IsNullOrWhiteSpace(cwd) ? null : NormalizePathSeparators(cwd);

        for (var i = 0; i < normalizedArgs.Count; i++)
            normalizedArgs[i] = RemapKnownPackagePath(normalizedArgs[i]);

        if (!string.IsNullOrWhiteSpace(normalizedCwd))
            normalizedCwd = RemapKnownPackagePath(normalizedCwd);

        return (normalizedCommand, normalizedArgs, normalizedCwd);
    }

    private static string NormalizeCommand(string command)
    {
        var value = NormalizePathSeparators(command).Trim();
        if (string.IsNullOrWhiteSpace(value))
            return value;

        var fileName = Path.GetFileName(value);
        if (fileName.Equals("node.exe", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("node", StringComparison.OrdinalIgnoreCase))
        {
            return "node";
        }

        if (fileName.Equals("npx.cmd", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("npx", StringComparison.OrdinalIgnoreCase))
        {
            return "npx";
        }

        if (fileName.Equals("python.exe", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("python", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("python3", StringComparison.OrdinalIgnoreCase))
        {
            return "python3";
        }

        return RemapKnownPackagePath(value);
    }

    private static string NormalizePathSeparators(string path) =>
        path.Replace('\\', '/');

    private static string RemapKnownPackagePath(string path)
    {
        var normalized = NormalizePathSeparators(path);

        // Cursor local zuora-mcp runtime → packaged path inside sidecar volume.
        var marker = "/zuora-mcp-runtime/node_modules/zuora-mcp/";
        var idx = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            var relative = normalized[(idx + marker.Length)..];
            return "/opt/mcps/zuora-mcp/" + relative.TrimStart('/');
        }

        marker = "/node_modules/zuora-mcp/";
        idx = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            var relative = normalized[(idx + marker.Length)..];
            return "/opt/mcps/zuora-mcp/" + relative.TrimStart('/');
        }

        if (normalized.Contains("/zuora-mcp/dist/", StringComparison.OrdinalIgnoreCase)
            && !normalized.StartsWith("/opt/mcps/", StringComparison.OrdinalIgnoreCase))
        {
            var distIdx = normalized.IndexOf("/zuora-mcp/", StringComparison.OrdinalIgnoreCase);
            if (distIdx >= 0)
                return "/opt/mcps" + normalized[distIdx..];
        }

        return normalized;
    }
}
