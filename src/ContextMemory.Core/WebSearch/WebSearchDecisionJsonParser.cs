using System.Text.Json;
using System.Text.Json.Serialization;

namespace ContextMemory.Core.WebSearch;

internal static class WebSearchDecisionJsonParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static WebSearchDecision? TryParse(string raw)
    {
        var payload = Session.SessionWikiJsonParser.ExtractJsonPayload(raw);
        if (string.IsNullOrWhiteSpace(payload))
            return null;

        try
        {
            var parsed = JsonSerializer.Deserialize<LlmDecisionDto>(payload, JsonOptions);
            if (parsed is null)
                return null;

            if (!parsed.NeedsWebSearch)
                return WebSearchDecision.Skip("llm_declined", "llm");

            var query = parsed.SearchQuery?.Trim();
            if (string.IsNullOrWhiteSpace(query))
                return null;

            return WebSearchDecision.Search(query, "llm");
        }
        catch
        {
            return null;
        }
    }

    private sealed class LlmDecisionDto
    {
        [JsonPropertyName("needs_web_search")]
        public bool NeedsWebSearch { get; set; }

        [JsonPropertyName("search_query")]
        public string? SearchQuery { get; set; }
    }
}
