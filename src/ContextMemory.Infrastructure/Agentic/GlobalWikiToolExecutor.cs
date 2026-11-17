using System.Text;
using System.Text.Json;
using ContextMemory.Core.Agentic;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.GlobalWiki;
using ContextMemory.Core.Models;

namespace ContextMemory.Infrastructure.Agentic;

public sealed class GlobalWikiToolExecutor : IToolExecutor, IToolDescriptor
{
    private readonly GlobalWikiService _wikiService;

    public GlobalWikiToolExecutor(GlobalWikiService wikiService) => _wikiService = wikiService;

    public IEnumerable<ToolRegistration> Describe() =>
    [
        new(AgenticToolRegistry.WikiSearchToolName, ToolCapabilities.QueryFocused, ToolHandlerKind.AppScoped),
        new(AgenticToolRegistry.WikiFetchToolName, ToolCapabilities.QueryFocused, ToolHandlerKind.AppScoped),
        new(AgenticToolRegistry.WikiGrepToolName, ToolCapabilities.QueryFocused, ToolHandlerKind.AppScoped)
    ];

    public IEnumerable<OllamaTool> BuildSchemas(AppRuntimeConfig runtimeConfig)
    {
        if (AgenticToolRegistry.BuildWikiSearchTool(runtimeConfig, lazySchemas: false) is { } search)
            yield return search;
        if (AgenticToolRegistry.BuildWikiFetchTool(runtimeConfig, lazySchemas: false) is { } fetch)
            yield return fetch;
        if (AgenticToolRegistry.BuildWikiGrepTool(runtimeConfig, lazySchemas: false) is { } grep)
            yield return grep;
    }

    public bool CanExecute(string toolName, AppRuntimeConfig runtimeConfig) =>
        runtimeConfig.GlobalWikiEnabled
        && Describe().Any(r => string.Equals(r.WireName, toolName, StringComparison.OrdinalIgnoreCase));

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

        if (string.Equals(toolCall.Function.Name, AgenticToolRegistry.WikiGrepToolName, StringComparison.Ordinal))
            return ExecuteGrepAsync(toolCall, appId, runtimeConfig, cancellationToken);

        if (string.Equals(toolCall.Function.Name, AgenticToolRegistry.WikiFetchToolName, StringComparison.Ordinal))
            return ExecuteFetchAsync(toolCall, appId, runtimeConfig, cancellationToken);

        return ExecuteSearchAsync(toolCall, appId, cancellationToken);
    }

    private async Task<ToolExecutionResult> ExecuteSearchAsync(
        OllamaToolCall toolCall,
        string appId,
        CancellationToken cancellationToken)
    {
        string query;
        string? sourceId = null;
        DateTimeOffset? asOf = null;
        var topK = AgenticToolRegistry.WikiSearchMapMaxTopK;
        try
        {
            using var doc = JsonDocument.Parse(
                string.IsNullOrWhiteSpace(toolCall.Function.Arguments) ? "{}" : toolCall.Function.Arguments);
            var root = doc.RootElement;
            query = AgenticToolArguments.GetString(root, "query") ?? string.Empty;
            sourceId = AgenticToolArguments.GetString(root, "sourceId");
            topK = AgenticToolArguments.GetInt(root, "topK", topK);
            if (DateTimeOffset.TryParse(
                    AgenticToolArguments.GetString(root, "asOf"),
                    out var asOfVal))
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

        topK = Math.Clamp(topK, 1, AgenticToolRegistry.WikiSearchMapMaxTopK);

        var result = await _wikiService.SearchMapAsync(
            appId,
            new GlobalWikiQueryRequest
            {
                Query = query,
                SourceId = sourceId,
                TopK = topK,
                AsOf = asOf,
                DigestOnly = true
            },
            cancellationToken).ConfigureAwait(false);

        return new ToolExecutionResult
        {
            Output = result.MapMarkdown,
            ExitCode = 0
        };
    }

    private async Task<ToolExecutionResult> ExecuteFetchAsync(
        OllamaToolCall toolCall,
        string appId,
        AppRuntimeConfig runtimeConfig,
        CancellationToken cancellationToken)
    {
        string id;
        try
        {
            using var doc = JsonDocument.Parse(
                string.IsNullOrWhiteSpace(toolCall.Function.Arguments) ? "{}" : toolCall.Function.Arguments);
            var root = doc.RootElement;
            id = AgenticToolArguments.GetString(root, "id")
                 ?? AgenticToolArguments.GetString(root, "documentId")
                 ?? string.Empty;
        }
        catch
        {
            return new ToolExecutionResult
            {
                Output = "Invalid wiki_fetch arguments. Expected JSON with an \"id\" field.",
                ExitCode = 1
            };
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            return new ToolExecutionResult
            {
                Output = "wiki_fetch requires a non-empty \"id\".",
                ExitCode = 1
            };
        }

        var document = await _wikiService.GetAsync(appId, id.Trim(), cancellationToken).ConfigureAwait(false);
        if (document is null)
        {
            return new ToolExecutionResult
            {
                Output = $"Document '{id}' not found (inactive or missing).",
                ExitCode = 1
            };
        }

        var budget = runtimeConfig.MaxGlobalWikiToolChars > 0
            ? runtimeConfig.MaxGlobalWikiToolChars
            : GlobalWikiService.DefaultBudgetChars;

        var title = string.IsNullOrWhiteSpace(document.Title) ? document.DocumentId : document.Title.Trim();
        var sb = new StringBuilder();
        sb.Append("# ").Append(title).AppendLine();
        sb.Append("id: `").Append(document.DocumentId).AppendLine("`");
        if (!string.IsNullOrWhiteSpace(document.SourceId))
            sb.Append("sourceId: ").AppendLine(document.SourceId);
        sb.AppendLine();
        sb.Append(document.Content ?? string.Empty);

        var output = sb.ToString().TrimEnd();
        var truncated = false;
        if (output.Length > budget)
        {
            output = output[..Math.Max(0, budget)] + "\n\n_(truncated)_";
            truncated = true;
        }

        return new ToolExecutionResult
        {
            Output = truncated ? output : output + (output.Length == 0 ? "(empty document)" : string.Empty),
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
            pattern = AgenticToolArguments.GetString(root, "pattern") ?? string.Empty;
            sourceId = AgenticToolArguments.GetString(root, "sourceId");
            maxHits = AgenticToolArguments.GetInt(root, "maxHits", maxHits);
            if (DateTimeOffset.TryParse(
                    AgenticToolArguments.GetString(root, "asOf"),
                    out var asOfVal))
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
