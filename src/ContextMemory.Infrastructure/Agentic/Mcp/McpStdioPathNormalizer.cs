namespace ContextMemory.Infrastructure.Agentic.Mcp;

/// <summary>
/// Rewrites common Cursor/Windows MCP command specs into Linux container paths for the mcp-runtime sidecar.
/// Maps any <c>.../node_modules/{package}/...</c> path to <c>/opt/mcps/{package}/...</c>.
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
        if (normalized.StartsWith("/opt/mcps/", StringComparison.OrdinalIgnoreCase))
            return normalized;

        // .../node_modules/{package}/rest → /opt/mcps/{package}/rest
        const string nodeModules = "/node_modules/";
        var nmIdx = normalized.IndexOf(nodeModules, StringComparison.OrdinalIgnoreCase);
        if (nmIdx >= 0)
        {
            var after = normalized[(nmIdx + nodeModules.Length)..];
            var slash = after.IndexOf('/');
            if (slash > 0)
            {
                var package = after[..slash];
                var relative = after[(slash + 1)..].TrimStart('/');
                return string.IsNullOrEmpty(relative)
                    ? $"/opt/mcps/{package}"
                    : $"/opt/mcps/{package}/{relative}";
            }

            if (after.Length > 0)
                return $"/opt/mcps/{after}";
        }

        // Already under a package folder with dist/ but not remapped (e.g. /foo/bar-mcp/dist/x)
        var distMarker = "/dist/";
        var distIdx = normalized.IndexOf(distMarker, StringComparison.OrdinalIgnoreCase);
        if (distIdx > 0)
        {
            var beforeDist = normalized[..distIdx];
            var lastSlash = beforeDist.LastIndexOf('/');
            if (lastSlash >= 0 && lastSlash < beforeDist.Length - 1)
            {
                var package = beforeDist[(lastSlash + 1)..];
                if (package.Contains("mcp", StringComparison.OrdinalIgnoreCase))
                {
                    var relative = normalized[(distIdx + 1)..].TrimStart('/'); // dist/...
                    return $"/opt/mcps/{package}/{relative}";
                }
            }
        }

        return normalized;
    }
}
