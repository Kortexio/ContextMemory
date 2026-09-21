using System.Text;
using System.Text.Json;
using ContextMemory.Core.Agentic.Mcp;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

/// <summary>
/// Avoids Ollama's native Qwen XML tool parser (which 500s on format drift) by keeping tools
/// in prompt/content and parsing with <see cref="ProseToolCallParser"/>.
/// </summary>
public static class ClientSideToolCalling
{
    public const string CatalogMarker = "## Tool catalog (JSON only; do not invent tools)";

    /// <summary>Max chars per tool description in the client-side catalog.</summary>
    public const int CatalogDescriptionMaxChars = 72;

    public static void EnsureCatalogInSystemPrompt(List<OllamaMessage> messages, IReadOnlyList<OllamaTool> tools)
    {
        if (tools.Count == 0)
            return;

        var system = messages.FirstOrDefault(m =>
            string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase));
        if (system is null)
            return;

        var catalog = BuildCatalog(tools);
        var content = system.Content ?? string.Empty;
        var markerIdx = content.IndexOf(CatalogMarker, StringComparison.Ordinal);
        if (markerIdx >= 0)
            content = content[..markerIdx].TrimEnd() + "\n\n" + catalog;
        else
            content = content.TrimEnd() + "\n\n" + catalog;

        var idx = messages.IndexOf(system);
        messages[idx] = system with { Content = content };
    }

    public static string BuildCatalog(IReadOnlyList<OllamaTool> tools)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CatalogMarker);
        sb.AppendLine(
            "When you need a tool, reply with ONLY one JSON object (no prose, no XML):");
        sb.AppendLine(
            """{"tool":"exact_tool_name","arguments":{...}}""");
        // Critical: never put <function>/<parameter>/<tool_call> examples in the prompt.
        // Ollama's Qwen XML tool parser 500s on format drift even when tools[] is omitted.
        sb.AppendLine(
            "Never emit XML tool tags (function/parameter/tool_call). JSON only.");
        sb.AppendLine();

        var builtins = new List<OllamaTool>();
        var mcpByServer = new SortedDictionary<string, List<(string ToolName, OllamaTool Tool)>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var tool in tools)
        {
            var name = tool.Function.Name ?? string.Empty;
            if (McpToolNaming.TryParseQualifiedName(name, out var server, out var shortName))
            {
                if (!mcpByServer.TryGetValue(server, out var list))
                    mcpByServer[server] = list = [];
                list.Add((shortName, tool));
            }
            else
            {
                builtins.Add(tool);
            }
        }

        foreach (var tool in builtins)
            AppendToolLine(sb, tool.Function.Name, tool);

        foreach (var (server, entries) in mcpByServer)
        {
            sb.Append("MCP `").Append(server).AppendLine("` — call as `server__tool`:");
            foreach (var (shortName, tool) in entries)
                AppendToolLine(sb, shortName, tool, indent: "  ");
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Compact "Available tools" line: builtins comma-joined; MCP grouped as
    /// <c>server: t1, t2</c> (short names) to avoid repeating long qualified prefixes.
    /// </summary>
    public static string FormatToolNamesSummary(IReadOnlyList<OllamaTool> tools)
    {
        var builtins = new List<string>();
        var mcpByServer = new SortedDictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var tool in tools)
        {
            var name = tool.Function?.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            if (McpToolNaming.TryParseQualifiedName(name, out var server, out var shortName))
            {
                if (!mcpByServer.TryGetValue(server, out var list))
                    mcpByServer[server] = list = [];
                list.Add(shortName);
            }
            else
            {
                builtins.Add(name);
            }
        }

        var parts = new List<string>(1 + mcpByServer.Count);
        if (builtins.Count > 0)
            parts.Add(string.Join(", ", builtins));
        foreach (var (server, shortNames) in mcpByServer)
            parts.Add($"{server}: {string.Join(", ", shortNames)}");

        return string.Join(" | ", parts);
    }

    /// <summary>
    /// Resolves a short or qualified MCP tool name against the turn catalog.
    /// Ambiguous short names return candidates instead of silently failing.
    /// </summary>
    public static ShortMcpNameResolution ResolveShortMcpName(
        string name,
        IReadOnlyCollection<string> catalogNames)
    {
        if (string.IsNullOrWhiteSpace(name) || catalogNames.Count == 0)
            return new ShortMcpNameResolution(ShortMcpNameKind.NotFound, null, []);

        var trimmed = name.Trim();
        if (catalogNames.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
        {
            var exact = catalogNames.First(n =>
                string.Equals(n, trimmed, StringComparison.OrdinalIgnoreCase));
            return new ShortMcpNameResolution(ShortMcpNameKind.Exact, exact, []);
        }

        if (McpToolNaming.TryParseQualifiedName(trimmed, out _, out _))
            return new ShortMcpNameResolution(ShortMcpNameKind.AlreadyQualified, trimmed, []);

        var matches = new List<string>();
        foreach (var candidate in catalogNames)
        {
            if (!McpToolNaming.TryParseQualifiedName(candidate, out _, out var shortName))
                continue;
            if (string.Equals(shortName, trimmed, StringComparison.OrdinalIgnoreCase))
                matches.Add(candidate);
        }

        return matches.Count switch
        {
            0 => new ShortMcpNameResolution(ShortMcpNameKind.NotFound, null, []),
            1 => new ShortMcpNameResolution(ShortMcpNameKind.Unique, matches[0], matches),
            _ => new ShortMcpNameResolution(ShortMcpNameKind.Ambiguous, null, matches)
        };
    }

    /// <summary>
    /// Expands a short MCP tool name to the unique qualified catalog entry when unambiguous.
    /// Returns null for missing or ambiguous names — prefer <see cref="ResolveShortMcpName"/> when
    /// callers need to surface ambiguity.
    /// </summary>
    public static string? TryExpandShortMcpName(string name, IReadOnlyCollection<string> catalogNames)
    {
        var resolved = ResolveShortMcpName(name, catalogNames);
        return resolved.Kind is ShortMcpNameKind.Exact or ShortMcpNameKind.Unique
            ? resolved.ResolvedName
            : null;
    }

    private static void AppendToolLine(
        StringBuilder sb,
        string displayName,
        OllamaTool tool,
        string indent = "")
    {
        var fn = tool.Function;
        sb.Append(indent).Append("- `").Append(displayName).Append('`');
        if (!string.IsNullOrWhiteSpace(fn.Description))
        {
            var desc = NeutralizeXmlTriggers(fn.Description.Trim());
            if (desc.Length > CatalogDescriptionMaxChars)
                desc = desc[..CatalogDescriptionMaxChars] + "…";
            sb.Append(": ").Append(desc);
        }

        sb.AppendLine();
        // Skip open stub schemas (empty properties) — full params appear after tool_describe pin.
        if (McpPinnedToolFactory.IsOpenStubParameters(fn.Parameters))
            return;

        var schema = CompactSchema(fn.Parameters);
        if (!string.IsNullOrWhiteSpace(schema))
            sb.Append(indent).Append("  params: ").AppendLine(NeutralizeXmlTriggers(schema));
    }

    /// <summary>
    /// Angle brackets in prompts can trip Ollama's Qwen XML tool parser even without tools[].
    /// </summary>
    public static string NeutralizeXmlTriggers(string text) =>
        string.IsNullOrEmpty(text)
            ? text
            : text.Replace("<", "(", StringComparison.Ordinal)
                .Replace(">", ")", StringComparison.Ordinal);

    /// <summary>
    /// Flatten structured tool_calls / role=tool into plain chat so Ollama never re-enters its XML parser.
    /// </summary>
    public static List<OllamaMessage> FlattenForClientSideWire(IReadOnlyList<OllamaMessage> messages)
    {
        var result = new List<OllamaMessage>(messages.Count);
        foreach (var m in messages)
        {
            if (m.ToolCalls is { Count: > 0 })
            {
                result.Add(new OllamaMessage
                {
                    Role = "assistant",
                    Content = SerializeToolCallsAsJson(m.ToolCalls)
                });
                continue;
            }

            if (string.Equals(m.Role, "tool", StringComparison.OrdinalIgnoreCase))
            {
                var body = m.Content ?? string.Empty;
                // Avoid angle-brackets in observations that models echo into broken XML tool args.
                body = SanitizeObservation(body);
                result.Add(new OllamaMessage
                {
                    Role = "user",
                    Content = "Tool result:\n" + body
                });
                continue;
            }

            result.Add(m);
        }

        return result;
    }

    public static string SanitizeObservation(string content)
    {
        if (string.IsNullOrEmpty(content))
            return content;

        // Cap and neutralize tags that break Qwen/Ollama XML tool parsers when echoed.
        var trimmed = content.Length > 6000 ? content[..6000] + "…" : content;
        return NeutralizeXmlTriggers(trimmed);
    }

    private static string SerializeToolCallsAsJson(IReadOnlyList<OllamaToolCall> toolCalls)
    {
        if (toolCalls.Count == 1)
        {
            var tc = toolCalls[0];
            object argsObj;
            try
            {
                argsObj = JsonSerializer.Deserialize<object>(tc.Function.Arguments) ?? new { };
            }
            catch
            {
                argsObj = tc.Function.Arguments;
            }

            return JsonSerializer.Serialize(new { tool = tc.Function.Name, arguments = argsObj });
        }

        var list = toolCalls.Select(tc =>
        {
            object argsObj;
            try
            {
                argsObj = JsonSerializer.Deserialize<object>(tc.Function.Arguments) ?? new { };
            }
            catch
            {
                argsObj = tc.Function.Arguments;
            }

            return new { tool = tc.Function.Name, arguments = argsObj };
        }).ToList();

        return JsonSerializer.Serialize(new { tool_calls = list });
    }

    private static string? CompactSchema(object? parameters)
    {
        if (parameters is null)
            return null;
        try
        {
            var json = parameters switch
            {
                string s => s,
                JsonElement el => el.GetRawText(),
                _ => JsonSerializer.Serialize(parameters)
            };
            if (json.Length > 400)
                json = json[..400] + "…";
            return json;
        }
        catch
        {
            return null;
        }
    }
}
