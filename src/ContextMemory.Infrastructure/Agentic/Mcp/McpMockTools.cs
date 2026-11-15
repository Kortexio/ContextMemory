using System.Text.Json;
using ContextMemory.Core.Agentic;
using ContextMemory.Core.Agentic.Mcp;
using ContextMemory.Core.Localization;

namespace ContextMemory.Infrastructure.Agentic.Mcp;

/// <summary>
/// Shared mock MCP catalog for tests and adversarial bench (CM2).
/// </summary>
internal static class McpMockTools
{
    public static IReadOnlyList<McpToolDefinition> List(IntegrationToolConfig server)
    {
        var tools = new List<McpToolDefinition>
        {
            new()
            {
                ServerName = server.Name,
                Name = "get_account",
                Description = "Get subscription account details (mock MCP).",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        accountId = new { type = "string", description = "Account id" }
                    },
                    required = new[] { "accountId" }
                }
            }
        };

        if (OffersQueryObjects(server))
        {
            tools.Add(new McpToolDefinition
            {
                ServerName = server.Name,
                Name = "query_objects",
                Description =
                    "Query CRM objects by type and filter. Requires objectType and a non-empty where clause.",
                InputSchema = new
                {
                    type = "object",
                    additionalProperties = false,
                    required = new[] { "objectType", "where" },
                    properties = new
                    {
                        objectType = new
                        {
                            type = "string",
                            @enum = new[] { "Account", "Invoice", "Subscription" },
                            description = "CRM object type to query"
                        },
                        where = new
                        {
                            type = "object",
                            description = "Filter map; at least one field required",
                            minProperties = 1,
                            additionalProperties = new { type = new[] { "string", "number", "boolean" } },
                            properties = new
                            {
                                id = new { type = "string" },
                                name = new { type = "string" },
                                status = new { type = "string" }
                            }
                        },
                        limit = new { type = "integer", minimum = 1, maximum = 100, @default = 10 }
                    }
                }
            });
        }

        return tools;
    }

    public static string Execute(IntegrationToolConfig server, string toolName, string argumentsJson)
    {
        if (toolName.Contains("fail", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(ToolExecutionMessages.McpMockToolFailed(server.Name, toolName));

        if (toolName.Contains("query_objects", StringComparison.OrdinalIgnoreCase))
            return ExecuteQueryObjects(server.Name, argumentsJson);

        if (toolName.Contains("get_account", StringComparison.OrdinalIgnoreCase))
            return ExecuteGetAccount(server.Name, argumentsJson);

        return $"[mock:{server.Name}] {toolName}({argumentsJson}) → ok";
    }

    private static bool OffersQueryObjects(IntegrationToolConfig server)
    {
        // Bench / explicit CRM mocks only — do not attach query_objects to legacy zuora get_account mocks.
        var hay = $"{server.Name} {server.Url} {server.Description}";
        return Contains(hay, "crm", "bench", "query_objects");
    }

    private static bool Contains(string hay, params string[] needles) =>
        needles.Any(n => hay.Contains(n, StringComparison.OrdinalIgnoreCase));

    private static string ExecuteQueryObjects(string serverName, string argumentsJson)
    {
        if (!TryParseObject(argumentsJson, out var root))
            return JsonError(serverName, "query_objects", "arguments must be a JSON object (got empty or invalid)");

        if (!TryGetPropertyIgnoreCase(root, "objectType", out var objectType)
            || objectType.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(objectType.GetString()))
            return JsonError(serverName, "query_objects", "missing required objectType");

        if (!TryGetPropertyIgnoreCase(root, "where", out var where)
            || where.ValueKind != JsonValueKind.Object
            || !where.EnumerateObject().Any())
            return JsonError(serverName, "query_objects", "missing required non-empty where");

        var type = objectType.GetString()!;
        if (!string.Equals(type, "Account", StringComparison.OrdinalIgnoreCase))
        {
            return JsonSerializer.Serialize(new
            {
                ok = true,
                server = serverName,
                objectType = type,
                rows = Array.Empty<object>(),
                note = "no rows for this objectType in mock"
            });
        }

        string? id = null;
        if (TryGetPropertyIgnoreCase(where, "id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
            id = idEl.GetString();

        if (string.Equals(id, "ACC-1001", StringComparison.OrdinalIgnoreCase))
        {
            return JsonSerializer.Serialize(new
            {
                ok = true,
                server = serverName,
                objectType = "Account",
                rows = new[]
                {
                    new { id = "ACC-1001", name = "Acme Staging Holdings", status = "Active" }
                }
            });
        }

        return JsonSerializer.Serialize(new
        {
            ok = true,
            server = serverName,
            objectType = "Account",
            rows = Array.Empty<object>()
        });
    }

    private static string ExecuteGetAccount(string serverName, string argumentsJson)
    {
        if (!TryParseObject(argumentsJson, out var root)
            || !TryGetPropertyIgnoreCase(root, "accountId", out var idEl)
            || idEl.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(idEl.GetString()))
            return JsonError(serverName, "get_account", "missing required accountId");

        var id = idEl.GetString()!;
        var name = string.Equals(id, "ACC-1001", StringComparison.OrdinalIgnoreCase)
            ? "Acme Staging Holdings"
            : "Mock Account";
        return JsonSerializer.Serialize(new
        {
            ok = true,
            server = serverName,
            accountId = id,
            status = "Active",
            name
        });
    }

    private static string JsonError(string server, string tool, string message) =>
        JsonSerializer.Serialize(new { ok = false, server, tool, error = message });

    private static bool TryParseObject(string? json, out JsonElement root)
    {
        root = default;
        if (string.IsNullOrWhiteSpace(json) || json.Trim() is "{}" or "null")
            return false;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return false;
            root = doc.RootElement.Clone();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement obj, string name, out JsonElement value)
    {
        if (obj.TryGetProperty(name, out value))
            return true;
        foreach (var p in obj.EnumerateObject())
        {
            if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = p.Value;
                return true;
            }
        }
        value = default;
        return false;
    }
}
