using System.Text.Json;
using ContextMemory.Core.Agentic;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.GlobalWiki;
using ContextMemory.Core.Models;

namespace ContextMemory.Infrastructure.Agentic;

public sealed class GlobalWikiToolExecutor : IToolExecutor
{
    private readonly GlobalWikiService _wikiService;

    public GlobalWikiToolExecutor(GlobalWikiService wikiService) => _wikiService = wikiService;

    public bool CanExecute(string toolName, AppRuntimeConfig runtimeConfig) =>
        runtimeConfig.GlobalWikiEnabled
        && (string.Equals(toolName, AgenticToolRegistry.WikiSearchToolName, StringComparison.Ordinal)
            || string.Equals(toolName, AgenticToolRegistry.WikiGrepToolName, StringComparison.Ordinal));

    public Task<ToolExecutionResult> ExecuteAsync(
        OllamaToolCall toolCall,
        string appId,
        AppRuntimeConfig runtimeConfig,
        CancellationToken cancellationToken = default)
    {
        if (!CanExecute(toolCall.Function.Name, runtimeConfig))
        {
            return Task.FromResult(new ToolExecutionResult
            {
                Output = $"{toolCall.Function.Name} is not available for this app.",
                ExitCode = 1
            });
        }

        return string.Equals(toolCall.Function.Name, AgenticToolRegistry.WikiGrepToolName, StringComparison.Ordinal)
            ? ExecuteGrepAsync(toolCall, appId, runtimeConfig, cancellationToken)
            : ExecuteSearchAsync(toolCall, appId, runtimeConfig, cancellationToken);
    }

    private async Task<ToolExecutionResult> ExecuteSearchAsync(
        OllamaToolCall toolCall,
        string appId,
        AppRuntimeConfig runtimeConfig,
        CancellationToken cancellationToken)
    {
        string query;
        string? sourceId = null;
        DateTimeOffset? asOf = null;
        var topK = GlobalWikiService.DefaultTopK;
        try
        {
            using var doc = JsonDocument.Parse(
                string.IsNullOrWhiteSpace(toolCall.Function.Arguments) ? "{}" : toolCall.Function.Arguments);
            var root = doc.RootElement;
            query = root.TryGetProperty("query", out var q) ? q.GetString() ?? string.Empty : string.Empty;
            if (root.TryGetProperty("sourceId", out var s) && s.ValueKind == JsonValueKind.String)
                sourceId = s.GetString();
            if (root.TryGetProperty("topK", out var t) && t.TryGetInt32(out var topKVal) && topKVal > 0)
                topK = topKVal;
            if (root.TryGetProperty("asOf", out var a) && a.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(a.GetString(), out var asOfVal))
                asOf = asOfVal;
        }
        catch
        {
            return new ToolExecutionResult
            {
                Output = "Invalid wiki_search arguments. Expected JSON with a \"query\" field.",
                ExitCode = 1
            };
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return new ToolExecutionResult
            {
                Output = "wiki_search requires a non-empty \"query\".",
                ExitCode = 1
            };
        }

        var budget = runtimeConfig.MaxGlobalWikiToolChars > 0
            ? runtimeConfig.MaxGlobalWikiToolChars
            : GlobalWikiService.DefaultBudgetChars;

        var result = await _wikiService.QueryAsync(
            appId,
            new GlobalWikiQueryRequest
            {
                Query = query,
                SourceId = sourceId,
                TopK = topK,
                BudgetChars = budget,
                IncludeIndex = false,
                AsOf = asOf
            },
            budget,
            cancellationToken).ConfigureAwait(false);

        if (result.TotalDocuments == 0 || result.Matches.Count == 0)
        {
            return new ToolExecutionResult
            {
                Output = "No matching documents found in the app knowledge base.",
                ExitCode = 0
            };
        }

        var header = $"Found {result.Matches.Count} match(es) of {result.TotalDocuments} document(s) (asOf={result.AsOf:O}).\n\n";
        return new ToolExecutionResult
        {
            Output = header + result.CompiledMarkdown,
            ExitCode = 0
        };
    }

    private async Task<ToolExecutionResult> ExecuteGrepAsync(
        OllamaToolCall toolCall,
        string appId,
        AppRuntimeConfig runtimeConfig,
        CancellationToken cancellationToken)
    {
        string pattern;
        string? sourceId = null;
        DateTimeOffset? asOf = null;
        var maxHits = 40;
        try
        {
            using var doc = JsonDocument.Parse(
                string.IsNullOrWhiteSpace(toolCall.Function.Arguments) ? "{}" : toolCall.Function.Arguments);
            var root = doc.RootElement;
            pattern = root.TryGetProperty("pattern", out var p) ? p.GetString() ?? string.Empty : string.Empty;
            if (root.TryGetProperty("sourceId", out var s) && s.ValueKind == JsonValueKind.String)
                sourceId = s.GetString();
            if (root.TryGetProperty("maxHits", out var m) && m.TryGetInt32(out var maxHitsVal) && maxHitsVal > 0)
                maxHits = maxHitsVal;
            if (root.TryGetProperty("asOf", out var a) && a.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(a.GetString(), out var asOfVal))
                asOf = asOfVal;
        }
        catch
        {
            return new ToolExecutionResult
            {
                Output = "Invalid wiki_grep arguments. Expected JSON with a \"pattern\" field.",
                ExitCode = 1
            };
        }

        if (string.IsNullOrWhiteSpace(pattern))
        {
            return new ToolExecutionResult
            {
                Output = "wiki_grep requires a non-empty \"pattern\".",
                ExitCode = 1
            };
        }

        var budget = runtimeConfig.MaxGlobalWikiToolChars > 0
            ? runtimeConfig.MaxGlobalWikiToolChars
            : GlobalWikiService.DefaultBudgetChars;

        var result = await _wikiService.GrepAsync(
            appId,
            new GlobalWikiGrepRequest
            {
                Pattern = pattern,
                SourceId = sourceId,
                MaxHits = maxHits,
                AsOf = asOf,
                BudgetChars = budget
            },
            budget,
            cancellationToken).ConfigureAwait(false);

        return new ToolExecutionResult
        {
            Output = result.CompiledMarkdown
                     + (result.Truncated ? "\n\n_(results truncated)_" : string.Empty),
            ExitCode = 0
        };
    }
}
