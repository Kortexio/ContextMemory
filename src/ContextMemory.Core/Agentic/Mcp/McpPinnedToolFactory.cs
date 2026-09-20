using ContextMemory.Core.Agentic.Prompts;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic.Mcp;

/// <summary>Builds <see cref="OllamaTool"/> entries for MCP tools after lazy discovery (real schema from catalog).</summary>
public static class McpPinnedToolFactory
{
    public static OllamaTool Create(McpToolDefinition mcpTool, AppRuntimeConfig runtimeConfig)
    {
        var fullDescription = AgenticToolDescriptionBuilder.BuildMcpDescription(mcpTool, runtimeConfig);
        var parameters = mcpTool.InputSchema is null
            ? OpenStubParameters()
            : McpInputSchemaSanitizer.Sanitize(mcpTool.InputSchema);

        return new OllamaTool(
            "function",
            new OllamaFunction(
                mcpTool.QualifiedName,
                SessionDiscoveryTools.ShortenDescription(fullDescription),
                parameters));
    }

    public static object OpenStubParameters() => new Dictionary<string, object?>
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object?>(),
        ["additionalProperties"] = true
    };

    public static bool IsOpenStubParameters(object? parameters)
    {
        if (parameters is null)
            return true;

        if (parameters is Dictionary<string, object?> dict
            && dict.TryGetValue("properties", out var props))
        {
            if (props is Dictionary<string, object?> nested && nested.Count == 0)
                return true;
            if (props is System.Collections.IDictionary idict && idict.Count == 0)
                return true;
        }

        try
        {
            var json = parameters switch
            {
                string s => s,
                System.Text.Json.JsonElement el => el.GetRawText(),
                _ => System.Text.Json.JsonSerializer.Serialize(parameters)
            };
            return json.Contains("\"properties\":{}", StringComparison.Ordinal)
                   || json.Contains("\"properties\": {}", StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }
}
