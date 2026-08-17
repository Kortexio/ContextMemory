using System.Text;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Localization;
using ContextMemory.Core.Models;
using ContextMemory.Core.Session;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ContextMemory.Core.GlobalWiki;

public interface IGlobalWikiDigestGenerator
{
    Task<string> GenerateAsync(
        string appId,
        string documentId,
        string? title,
        string? sourceId,
        string content,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Uses the summary LLM (<see cref="AppRuntimeConfig.WikiLlmModel"/>) to build a keyword + ≤8-line digest.
/// </summary>
public sealed class GlobalWikiDigestGenerator : IGlobalWikiDigestGenerator
{
    public const int MaxLines = 8;
    public const int MaxChars = 2_000;

    private readonly ILlmAdapterResolver _adapterResolver;
    private readonly IAppConfigStore _appConfigStore;
    private readonly ContextMemoryOptions _options;
    private readonly ILogger<GlobalWikiDigestGenerator> _logger;

    public GlobalWikiDigestGenerator(
        ILlmAdapterResolver adapterResolver,
        IAppConfigStore appConfigStore,
        IOptions<ContextMemoryOptions> options,
        ILogger<GlobalWikiDigestGenerator> logger)
    {
        _adapterResolver = adapterResolver;
        _appConfigStore = appConfigStore;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> GenerateAsync(
        string appId,
        string documentId,
        string? title,
        string? sourceId,
        string content,
        CancellationToken cancellationToken = default)
    {
        var resolvedTitle = string.IsNullOrWhiteSpace(title)
            ? GlobalWikiSlug.ExtractTitle(content, documentId)
            : title.Trim();

        try
        {
            var config = _appConfigStore.GetConfig(appId);
            var adapter = _adapterResolver.Resolve(config);
            var model = SessionWikiSettings.ResolveWikiLlmModel(config, _options.DefaultWikiLlmModel);
            var prompt = LlmPrompts.WikiTicketDigest(config.DefaultLanguage)
                .Replace("{documentId}", documentId)
                .Replace("{title}", resolvedTitle)
                .Replace("{sourceId}", string.IsNullOrWhiteSpace(sourceId) ? "(none)" : sourceId.Trim())
                .Replace("{content}", content ?? string.Empty);

            var response = await adapter.GenerateAsync(
                new OllamaGenerateRequest
                {
                    Model = model,
                    Prompt = prompt,
                    Stream = false
                },
                cancellationToken).ConfigureAwait(false);

            var raw = OllamaLlmText.NormalizeAssistantContent(OllamaLlmText.GetGenerateText(response));
            var normalized = NormalizeDigest(raw, documentId, resolvedTitle);
            if (!string.IsNullOrWhiteSpace(normalized))
                return normalized;

            _logger.LogWarning(
                "Wiki digest LLM returned empty/unusable text for {AppId}/{DocumentId}; using fallback",
                appId,
                documentId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Wiki digest LLM failed for {AppId}/{DocumentId}; using fallback",
                appId,
                documentId);
        }

        return BuildFallbackDigest(documentId, resolvedTitle, content);
    }

    public static string NormalizeDigest(string? raw, string documentId, string title)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var lines = raw
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.None)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .Select(StripBulletPrefix)
            .Where(l => l.Length > 0)
            .Take(MaxLines)
            .ToList();

        if (lines.Count == 0)
            return string.Empty;

        if (!lines[0].StartsWith("Keywords:", StringComparison.OrdinalIgnoreCase))
            lines.Insert(0, $"Keywords: {documentId}, {title}");

        while (lines.Count > MaxLines)
            lines.RemoveAt(lines.Count - 1);

        var text = string.Join('\n', lines);
        return text.Length <= MaxChars ? text : text[..MaxChars].TrimEnd() + "…";
    }

    public static string BuildFallbackDigest(string documentId, string title, string? content)
    {
        var sb = new StringBuilder();
        sb.Append("Keywords: ").Append(documentId);
        if (!string.IsNullOrWhiteSpace(title) && !title.Equals(documentId, StringComparison.OrdinalIgnoreCase))
            sb.Append(", ").Append(title);

        var ruleLines = ExtractRuleishLines(content)
            .Take(MaxLines - 1)
            .ToList();

        if (ruleLines.Count == 0)
        {
            var excerpt = GlobalWikiSlug.ExtractSummary(content ?? string.Empty, explicitSummary: null);
            if (!string.IsNullOrWhiteSpace(excerpt))
                ruleLines.Add(excerpt);
        }

        foreach (var line in ruleLines)
            sb.Append('\n').Append(line);

        var text = sb.ToString();
        return text.Length <= MaxChars ? text : text[..MaxChars].TrimEnd() + "…";
    }

    private static IEnumerable<string> ExtractRuleishLines(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            yield break;

        string[] markers =
        [
            "regra", "rule", "must", "should", "não pode", "nao pode", "cannot", "sla",
            "obrigat", "política", "politica", "policy", "comment", "coment"
        ];

        foreach (var rawLine in content.Split('\n'))
        {
            var line = StripBulletPrefix(rawLine.Trim());
            if (line.Length < 8 || line.StartsWith('#'))
                continue;

            var lower = line.ToLowerInvariant();
            if (markers.Any(m => lower.Contains(m, StringComparison.Ordinal)))
                yield return line.Length <= 220 ? line : line[..220].TrimEnd() + "…";
        }
    }

    private static string StripBulletPrefix(string line)
    {
        if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal))
            return line[2..].Trim();
        if (line.Length > 2 && char.IsDigit(line[0]) && line.Contains(". ", StringComparison.Ordinal))
        {
            var idx = line.IndexOf(". ", StringComparison.Ordinal);
            if (idx is > 0 and < 4)
                return line[(idx + 2)..].Trim();
        }

        return line;
    }
}
