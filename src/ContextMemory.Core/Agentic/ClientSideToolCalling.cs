using System.Text;
using System.Text.Json;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

/// <summary>
/// Avoids Ollama's native Qwen XML tool parser (which 500s on format drift) by keeping tools
/// in prompt/content and parsing with <see cref="ProseToolCallParser"/>.
/// </summary>
public static class ClientSideToolCalling
{
    public const string CatalogMarker = "## Tool catalog (JSON only; do not invent tools)";

    public static void EnsureCatalogInSystemPrompt(List<OllamaMessage> messages, IReadOnlyList<OllamaTool> tools)
    {
        if (tools.Count == 0)
            return;

        var system = messages.FirstOrDefault(m =>
            string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase));
        if (system is null)
            return;

        if (system.Content?.Contains(CatalogMarker, StringComparison.Ordinal) == true)
            return;

        var catalog = BuildCatalog(tools);
        var idx = messages.IndexOf(system);
        messages[idx] = system with
        {
            Content = (system.Content ?? string.Empty).TrimEnd() + "\n\n" + catalog
        };
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

        foreach (var tool in tools)
        {
            var fn = tool.Function;
            sb.Append("- `").Append(fn.Name).Append('`');
            if (!string.IsNullOrWhiteSpace(fn.Description))
            {
                var desc = NeutralizeXmlTriggers(fn.Description.Trim());
                if (desc.Length > 160)
                    desc = desc[..160] + "…";
                sb.Append(": ").Append(desc);
            }

            sb.AppendLine();
            var schema = CompactSchema(fn.Parameters);
            if (!string.IsNullOrWhiteSpace(schema))
                sb.AppendLine("  params: " + NeutralizeXmlTriggers(schema));
        }

        return sb.ToString().TrimEnd();
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
