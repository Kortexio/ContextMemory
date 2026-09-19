using System.Net.Http;
using System.Text.RegularExpressions;

namespace ContextMemory.Core.Agentic;

/// <summary>Parses llama.cpp / Ollama / OpenAI context-window HTTP 400 bodies.</summary>
public static partial class LlmContextLengthError
{
    public static bool IsMatch(HttpRequestException ex)
    {
        var msg = ex.Message ?? string.Empty;
        return msg.Contains("exceed_context_size_error", StringComparison.OrdinalIgnoreCase)
               || msg.Contains("exceeds the available context size", StringComparison.OrdinalIgnoreCase)
               || msg.Contains("context_length_exceeded", StringComparison.OrdinalIgnoreCase)
               || msg.Contains("maximum context length", StringComparison.OrdinalIgnoreCase);
    }

    public static int? TryGetContextSize(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        var nCtx = NCtxRegex().Match(body);
        if (nCtx.Success && int.TryParse(nCtx.Groups[1].Value, out var parsed) && parsed > 0)
            return parsed;

        var available = AvailableSizeRegex().Match(body);
        if (available.Success && int.TryParse(available.Groups[1].Value, out parsed) && parsed > 0)
            return parsed;

        return null;
    }

    public static int? TryGetPromptTokens(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        var match = PromptTokensRegex().Match(body);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var parsed) && parsed > 0)
            return parsed;

        var requestTokens = RequestTokensRegex().Match(body);
        if (requestTokens.Success && int.TryParse(requestTokens.Groups[1].Value, out parsed) && parsed > 0)
            return parsed;

        return null;
    }

    [GeneratedRegex(@"n_ctx""?\s*[:=]\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NCtxRegex();

    [GeneratedRegex(@"available context size \((\d+) tokens\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AvailableSizeRegex();

    [GeneratedRegex(@"n_prompt_tokens""?\s*[:=]\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PromptTokensRegex();

    [GeneratedRegex(@"request \((\d+) tokens\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RequestTokensRegex();
}
