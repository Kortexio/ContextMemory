using ContextMemory.Core.Agentic.Mcp;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Contracts;

public interface IMcpToolSelector
{
    IReadOnlyList<McpToolDefinition> SelectTools(
        AppRuntimeConfig runtimeConfig,
        IReadOnlyList<McpToolDefinition> tools,
        string? userQuery,
        IReadOnlyList<string>? recentToolNames = null);
}
