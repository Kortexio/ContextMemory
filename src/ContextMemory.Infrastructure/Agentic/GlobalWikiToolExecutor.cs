using System.Text.Json;
using ContextMemory.Core.Agentic;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.GlobalWiki;
using ContextMemory.Core.Models;

namespace ContextMemory.Infrastructure.Agentic;

public sealed class GlobalWikiToolExecutor : IToolExecutor
{
    public const string ToolName = AgenticToolRegistry.WikiSearchToolName;

    private readonly GlobalWikiService _wikiService;

    public GlobalWikiToolExecutor(GlobalWikiService wikiService) => _wikiService = wikiService;

    public bool CanExecute(string toolName, AppRuntimeConfig runtimeConfig) =>
        runtimeConfig.GlobalWikiEnabled
        && string.Equals(toolName, ToolName, StringComparison.Ordinal);

    public async Task<ToolExecutionResult> ExecuteAsync(
        OllamaToolCall toolCall,
        string appId,
        AppRuntimeConfig runtimeConfig,
        CancellationToken cancellationToken = default)
    {
        if (!CanExecute(toolCall.Function.Name, runtimeConfig))
        {
            return new ToolExecutionResult
            {
                Output = "wiki_search is not available for this app.",
                ExitCode = 1
            };
        }

        string query;
        string? sourceId = null;
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
                IncludeIndex = false
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

        var header = $"Found {result.Matches.Count} match(es) of {result.TotalDocuments} document(s).\n\n";
        return new ToolExecutionResult
        {
            Output = header + result.CompiledMarkdown,
            ExitCode = 0
        };
    }
}
