using System.Text;
using System.Text.Json;
using ContextMemory.Core.Agentic;
using ContextMemory.Core.Agentic.Mcp;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;

namespace ContextMemory.Infrastructure.Agentic;

public sealed class SessionDiscoveryToolExecutor : ISessionScopedToolExecutor
{
    public const string TodosArtifactId = "meta:todos";

    private readonly ISessionArtifactStore _artifacts;
    private readonly ISessionStore _sessionStore;
    private readonly IMcpToolCatalog _mcpCatalog;

    public SessionDiscoveryToolExecutor(
        ISessionArtifactStore artifacts,
        ISessionStore sessionStore,
        IMcpToolCatalog mcpCatalog)
    {
        _artifacts = artifacts;
        _sessionStore = sessionStore;
        _mcpCatalog = mcpCatalog;
    }

    public bool CanExecute(string toolName, AppRuntimeConfig runtimeConfig) =>
        SessionDiscoveryTools.IsDiscoveryTool(toolName)
        && !string.Equals(toolName, SessionDiscoveryTools.DelegateTask, StringComparison.OrdinalIgnoreCase);

    public async Task<ToolExecutionResult> ExecuteAsync(
        OllamaToolCall toolCall,
        string appId,
        string userId,
        string sessionId,
        AppRuntimeConfig runtimeConfig,
        Action<AgenticProgressEvent>? report = null,
        CancellationToken cancellationToken = default)
    {
        _ = report;
        var name = toolCall.Function.Name;
        using var doc = JsonDocument.Parse(
            string.IsNullOrWhiteSpace(toolCall.Function.Arguments) ? "{}" : toolCall.Function.Arguments);
        var root = doc.RootElement;

        if (string.Equals(name, SessionDiscoveryTools.ArtifactRead, StringComparison.OrdinalIgnoreCase))
        {
            var id = GetString(root, "artifactId");
            if (string.IsNullOrWhiteSpace(id))
                return Fail("artifact_read requires artifactId.");
            var content = await _artifacts.ReadAsync(appId, userId, sessionId, id, cancellationToken)
                .ConfigureAwait(false);
            return content is null
                ? Fail($"Artifact not found: {id}")
                : Ok(content);
        }

        if (string.Equals(name, SessionDiscoveryTools.ArtifactTail, StringComparison.OrdinalIgnoreCase))
        {
            var id = GetString(root, "artifactId");
            if (string.IsNullOrWhiteSpace(id))
                return Fail("artifact_tail requires artifactId.");
            var maxChars = GetInt(root, "maxChars", 2000);
            var content = await _artifacts.TailAsync(appId, userId, sessionId, id, maxChars, cancellationToken)
                .ConfigureAwait(false);
            return content is null
                ? Fail($"Artifact not found: {id}")
                : Ok(content);
        }

        if (string.Equals(name, SessionDiscoveryTools.SkillSearch, StringComparison.OrdinalIgnoreCase))
        {
            var query = GetString(root, "query");
            if (string.IsNullOrWhiteSpace(query))
                return Fail("skill_search requires query.");
            var maxResults = GetInt(root, "maxResults", 8);
            return Ok(SearchCatalog(
                runtimeConfig.ResolvedPolicy.ActiveSkills.Where(s => AgenticSkillActivation.IsSkill(s.Activation)),
                query,
                maxResults,
                "skills"));
        }

        if (string.Equals(name, SessionDiscoveryTools.SkillRead, StringComparison.OrdinalIgnoreCase))
        {
            var skillId = GetString(root, "skillId");
            if (string.IsNullOrWhiteSpace(skillId))
                return Fail("skill_read requires skillId.");

            var skill = runtimeConfig.ResolvedPolicy.ActiveSkills
                .FirstOrDefault(s =>
                    AgenticSkillActivation.IsSkill(s.Activation)
                    && string.Equals(s.Id, skillId, StringComparison.OrdinalIgnoreCase));
            if (skill is null)
                return Fail($"Skill not found or not active: {skillId}");

            var body = string.IsNullOrWhiteSpace(skill.PromptMarkdown)
                ? "(empty skill body)"
                : skill.PromptMarkdown.Trim();
            return Ok($"# Skill `{skill.Id}` — {skill.Name}\n\n{body}");
        }

        if (string.Equals(name, SessionDiscoveryTools.RuleSearch, StringComparison.OrdinalIgnoreCase))
        {
            var query = GetString(root, "query");
            if (string.IsNullOrWhiteSpace(query))
                return Fail("rule_search requires query.");
            var maxResults = GetInt(root, "maxResults", 8);
            return Ok(SearchCatalog(
                runtimeConfig.ResolvedPolicy.ActiveSkills.Where(s => AgenticSkillActivation.IsRequestable(s.Activation)),
                query,
                maxResults,
                "rules"));
        }

        if (string.Equals(name, SessionDiscoveryTools.RuleRead, StringComparison.OrdinalIgnoreCase))
        {
            var ruleId = GetString(root, "ruleId");
            if (string.IsNullOrWhiteSpace(ruleId))
                return Fail("rule_read requires ruleId.");

            var rule = runtimeConfig.ResolvedPolicy.ActiveSkills
                .FirstOrDefault(s =>
                    AgenticSkillActivation.IsRule(s.Activation)
                    && string.Equals(s.Id, ruleId, StringComparison.OrdinalIgnoreCase));
            if (rule is null)
                return Fail($"Rule not found or not active: {ruleId}");

            var body = string.IsNullOrWhiteSpace(rule.PromptMarkdown)
                ? "(empty rule body)"
                : rule.PromptMarkdown.Trim();
            return Ok($"# Rule `{rule.Id}` — {rule.Name} ({rule.Activation})\n\n{body}");
        }

        if (string.Equals(name, SessionDiscoveryTools.ToolDescribe, StringComparison.OrdinalIgnoreCase))
        {
            var toolName = GetString(root, "toolName");
            if (string.IsNullOrWhiteSpace(toolName))
                return Fail("tool_describe requires toolName.");

            var described = await DescribeToolAsync(toolName, runtimeConfig, cancellationToken)
                .ConfigureAwait(false);
            return described is null
                ? Fail($"Unknown tool: {toolName}")
                : Ok(described);
        }

        if (string.Equals(name, SessionDiscoveryTools.SessionLogSearch, StringComparison.OrdinalIgnoreCase))
        {
            var query = GetString(root, "query");
            if (string.IsNullOrWhiteSpace(query))
                return Fail("session_log_search requires query.");
            var maxChars = GetInt(root, "maxChars", 2000);
            var snapshot = await _sessionStore.LoadAsync(appId, userId, sessionId, cancellationToken)
                .ConfigureAwait(false);
            var hits = SearchLog(snapshot.LogMd, query, maxChars);
            return Ok(string.IsNullOrWhiteSpace(hits)
                ? $"No matches in session log for '{query}'."
                : hits);
        }

        if (string.Equals(name, SessionDiscoveryTools.TodoWrite, StringComparison.OrdinalIgnoreCase))
        {
            var json = root.TryGetProperty("todos", out var todosEl)
                ? todosEl.GetRawText()
                : "[]";
            await _artifacts
                .WriteAsync(appId, userId, sessionId, TodosArtifactId, json, cancellationToken)
                .ConfigureAwait(false);
            return Ok($"Todos updated ({TodosArtifactId}).\n{json}");
        }

        return Fail($"Unsupported discovery tool: {name}");
    }

    private async Task<string?> DescribeToolAsync(
        string toolName,
        AppRuntimeConfig runtimeConfig,
        CancellationToken cancellationToken)
    {
        if (SessionDiscoveryTools.IsDiscoveryTool(toolName))
        {
            var meta = SessionDiscoveryTools.BuildTools(runtimeConfig)
                .FirstOrDefault(t => string.Equals(t.Function.Name, toolName, StringComparison.OrdinalIgnoreCase));
            if (meta is not null)
                return FormatTool(meta);
        }

        var builtins = new List<OllamaTool>();
        builtins.AddRange(AgenticToolRegistry.BuildExecutionTools(runtimeConfig, lazySchemas: false));
        var wiki = AgenticToolRegistry.BuildWikiSearchTool(runtimeConfig, lazySchemas: false);
        if (wiki is not null)
            builtins.Add(wiki);
        var wikiGrep = AgenticToolRegistry.BuildWikiGrepTool(runtimeConfig, lazySchemas: false);
        if (wikiGrep is not null)
            builtins.Add(wikiGrep);
        builtins.AddRange(SessionDiscoveryTools.BuildTools(runtimeConfig));
        var built = builtins.FirstOrDefault(t =>
            string.Equals(t.Function.Name, toolName, StringComparison.OrdinalIgnoreCase));
        if (built is not null)
            return FormatTool(built);

        var mcp = await _mcpCatalog
            .GetToolsAsync(runtimeConfig, userQuery: toolName, recentToolNames: [toolName], cancellationToken)
            .ConfigureAwait(false);
        var match = mcp.FirstOrDefault(t =>
            string.Equals(t.QualifiedName, toolName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(t.Name, toolName, StringComparison.OrdinalIgnoreCase));
        if (match is null)
            return null;

        var sb = new StringBuilder();
        sb.AppendLine($"# Tool `{match.QualifiedName}`");
        sb.AppendLine(match.Description ?? string.Empty);
        sb.AppendLine();
        sb.AppendLine("## Input schema");
        sb.AppendLine("```json");
        sb.AppendLine(match.InputSchema is null
            ? "{}"
            : JsonSerializer.Serialize(
                McpInputSchemaSanitizer.Sanitize(match.InputSchema),
                new JsonSerializerOptions { WriteIndented = true }));
        sb.AppendLine("```");
        return sb.ToString();
    }

    private static string SearchCatalog(
        IEnumerable<AgenticSkillDefinition> items,
        string query,
        int maxResults,
        string label)
    {
        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var scored = new List<(int Score, AgenticSkillDefinition Item, string Snippet)>();
        foreach (var item in items)
        {
            var hay = $"{item.Id}\n{item.Name}\n{item.Description}\n{item.Category}\n{item.PromptMarkdown}";
            var score = 0;
            foreach (var t in tokens)
            {
                if (item.Id.Contains(t, StringComparison.OrdinalIgnoreCase))
                    score += 5;
                if (item.Name.Contains(t, StringComparison.OrdinalIgnoreCase))
                    score += 4;
                if (item.Description.Contains(t, StringComparison.OrdinalIgnoreCase))
                    score += 3;
                if (hay.Contains(t, StringComparison.OrdinalIgnoreCase))
                    score += 1;
            }

            if (score <= 0)
                continue;

            var snippet = string.IsNullOrWhiteSpace(item.Description)
                ? (item.PromptMarkdown.Length > 160 ? item.PromptMarkdown[..160] + "…" : item.PromptMarkdown)
                : item.Description;
            scored.Add((score, item, snippet.Trim()));
        }

        if (scored.Count == 0)
            return $"No matching {label} for '{query}'.";

        var sb = new StringBuilder();
        sb.AppendLine($"# {label} matches for `{query}`");
        foreach (var hit in scored.OrderByDescending(x => x.Score).Take(Math.Max(1, maxResults)))
            sb.AppendLine($"- `{hit.Item.Id}`: {hit.Item.Name} — {hit.Snippet}");
        return sb.ToString().TrimEnd();
    }

    private static string FormatTool(OllamaTool tool)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Tool `{tool.Function.Name}`");
        sb.AppendLine(tool.Function.Description ?? string.Empty);
        sb.AppendLine();
        sb.AppendLine("## Parameters");
        sb.AppendLine("```json");
        try
        {
            sb.AppendLine(JsonSerializer.Serialize(tool.Function.Parameters, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            sb.AppendLine("{}");
        }

        sb.AppendLine("```");
        return sb.ToString();
    }

    private static string SearchLog(string? logMd, string query, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(logMd))
            return string.Empty;

        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
            return string.Empty;

        var lines = logMd.Split('\n');
        var matches = new List<string>();
        foreach (var line in lines)
        {
            if (tokens.Any(t => line.Contains(t, StringComparison.OrdinalIgnoreCase)))
                matches.Add(line.TrimEnd());
        }

        if (matches.Count == 0)
            return string.Empty;

        var joined = string.Join('\n', matches);
        if (maxChars > 0 && joined.Length > maxChars)
            joined = joined[..maxChars] + "\n…";
        return joined;
    }

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    private static int GetInt(JsonElement root, string name, int fallback) =>
        root.TryGetProperty(name, out var el) && el.TryGetInt32(out var n) && n > 0 ? n : fallback;

    private static ToolExecutionResult Ok(string output) => new() { Output = output, ExitCode = 0 };

    private static ToolExecutionResult Fail(string message) => new() { Output = message, ExitCode = 1 };
}
