using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

/// <summary>Built-in tools for Cursor-style dynamic context discovery.</summary>
public static class SessionDiscoveryTools
{
    public const string ArtifactRead = "artifact_read";
    public const string ArtifactTail = "artifact_tail";
    public const string SkillRead = "skill_read";
    public const string SkillSearch = "skill_search";
    public const string RuleRead = "rule_read";
    public const string RuleSearch = "rule_search";
    public const string ToolDescribe = "tool_describe";
    public const string SessionLogSearch = "session_log_search";
    public const string DelegateTask = "delegate_task";
    public const string TodoWrite = "todo_write";

    public static bool IsDiscoveryTool(string? toolName) =>
        string.Equals(toolName, ArtifactRead, StringComparison.OrdinalIgnoreCase)
        || string.Equals(toolName, ArtifactTail, StringComparison.OrdinalIgnoreCase)
        || string.Equals(toolName, SkillRead, StringComparison.OrdinalIgnoreCase)
        || string.Equals(toolName, SkillSearch, StringComparison.OrdinalIgnoreCase)
        || string.Equals(toolName, RuleRead, StringComparison.OrdinalIgnoreCase)
        || string.Equals(toolName, RuleSearch, StringComparison.OrdinalIgnoreCase)
        || string.Equals(toolName, ToolDescribe, StringComparison.OrdinalIgnoreCase)
        || string.Equals(toolName, SessionLogSearch, StringComparison.OrdinalIgnoreCase)
        || string.Equals(toolName, DelegateTask, StringComparison.OrdinalIgnoreCase)
        || string.Equals(toolName, TodoWrite, StringComparison.OrdinalIgnoreCase);

    public static List<OllamaTool> BuildTools(AppRuntimeConfig runtimeConfig)
    {
        _ = runtimeConfig;
        return
        [
            new OllamaTool("function", new OllamaFunction(
                ArtifactRead,
                "Read a full session artifact previously saved from a long tool output. Pass artifactId from the observation pointer.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        artifactId = new { type = "string", description = "Id like tool:wiki_search:ab12cd34" }
                    },
                    required = new[] { "artifactId" }
                })),
            new OllamaTool("function", new OllamaFunction(
                ArtifactTail,
                "Read the end of a session artifact (like tail). Prefer this before artifact_read for large outputs.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        artifactId = new { type = "string", description = "Artifact id" },
                        maxChars = new { type = "integer", description = "Max characters from the end (default 2000)" }
                    },
                    required = new[] { "artifactId" }
                })),
            new OllamaTool("function", new OllamaFunction(
                SkillSearch,
                "Search active skills by keywords (id, name, description, body). Returns ids + snippets; then use skill_read.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        query = new { type = "string", description = "Keywords to match against skills" },
                        maxResults = new { type = "integer", description = "Max matches (default 8)" }
                    },
                    required = new[] { "query" }
                })),
            new OllamaTool("function", new OllamaFunction(
                SkillRead,
                "Load the full body of an active skill by id (skills are listed by id/name only in the system prompt).",
                new
                {
                    type = "object",
                    properties = new
                    {
                        skillId = new { type = "string", description = "Skill id from skill_search or defaults" }
                    },
                    required = new[] { "skillId" }
                })),
            new OllamaTool("function", new OllamaFunction(
                RuleSearch,
                "Search requestable rules (markdown policy docs). Always-on rules are already in the system prompt.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        query = new { type = "string", description = "Keywords to match against rules" },
                        maxResults = new { type = "integer", description = "Max matches (default 8)" }
                    },
                    required = new[] { "query" }
                })),
            new OllamaTool("function", new OllamaFunction(
                RuleRead,
                "Load a requestable rule by id.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        ruleId = new { type = "string", description = "Rule id from rule_search" }
                    },
                    required = new[] { "ruleId" }
                })),
            new OllamaTool("function", new OllamaFunction(
                ToolDescribe,
                "Load the full description/schema for a tool by name (MCP/builtins listed with short/open schemas).",
                new
                {
                    type = "object",
                    properties = new
                    {
                        toolName = new { type = "string", description = "Exact tool/function name" }
                    },
                    required = new[] { "toolName" }
                })),
            new OllamaTool("function", new OllamaFunction(
                SessionLogSearch,
                "Search the session log/transcript for keywords when the rolling summary is missing a detail.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        query = new { type = "string", description = "Case-insensitive substring or keywords" },
                        maxChars = new { type = "integer", description = "Max chars to return (default 2000)" }
                    },
                    required = new[] { "query" }
                })),
            new OllamaTool("function", new OllamaFunction(
                DelegateTask,
                "Spawn a depth-1 subagent in an isolated child session. Returns summary + artifactId with full transcript.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        task = new { type = "string", description = "Clear objective for the subagent" },
                        maxIterations = new { type = "integer", description = "Cap iterations for the child (default 4, max 8)" }
                    },
                    required = new[] { "task" }
                })),
            new OllamaTool("function", new OllamaFunction(
                TodoWrite,
                "Update the session todo list shown in Admin Chat Lab (replace entire list).",
                new
                {
                    type = "object",
                    properties = new
                    {
                        todos = new
                        {
                            type = "array",
                            description = "Todo items",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    id = new { type = "string" },
                                    content = new { type = "string" },
                                    status = new { type = "string", description = "pending|in_progress|completed|cancelled" }
                                }
                            }
                        }
                    },
                    required = new[] { "todos" }
                }))
        ];
    }

    /// <summary>Short one-line MCP description for lazy discovery (full text via tool_describe).</summary>
    public static string ShortenDescription(string? description, int maxChars = 120)
    {
        if (string.IsNullOrWhiteSpace(description))
            return "Use tool_describe for full schema/description.";

        var trimmed = description.Trim().Replace("\r", " ").Replace("\n", " ");
        while (trimmed.Contains("  ", StringComparison.Ordinal))
            trimmed = trimmed.Replace("  ", " ", StringComparison.Ordinal);

        if (trimmed.Length <= maxChars)
            return trimmed + " (tool_describe for full schema)";

        var keep = Math.Clamp(maxChars - 32, 1, trimmed.Length);
        return trimmed[..keep].TrimEnd() + "… (tool_describe for full schema)";
    }
}
