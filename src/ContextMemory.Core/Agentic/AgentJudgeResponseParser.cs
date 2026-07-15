using System.Text.Json;

using ContextMemory.Core.Localization;

namespace ContextMemory.Core.Agentic;

internal static class AgentJudgeResponseParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static ValidationResult Parse(string raw, string? language = null)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return ValidationResult.Ok();

        var json = ExtractJson(raw);
        if (json is null)
            return ValidationResult.Ok();

        try
        {
            var parsed = JsonSerializer.Deserialize<AgentJudgeResponse>(json, JsonOptions);
            if (parsed is null)
                return ValidationResult.Ok();

            return parsed.Valid
                ? ValidationResult.Ok()
                : ValidationResult.Reject(
                    string.IsNullOrWhiteSpace(parsed.Feedback)
                        ? AgenticMessages.JudgeDefaultReject(language)
                        : parsed.Feedback.Trim());
        }
        catch (JsonException)
        {
            return ValidationResult.Ok();
        }
    }

    private static string? ExtractJson(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.StartsWith('{'))
            return trimmed;

        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start >= 0 && end > start)
            return trimmed[start..(end + 1)];

        return null;
    }

    private sealed class AgentJudgeResponse
    {
        public bool Valid { get; init; }
        public string? Feedback { get; init; }
    }
}
