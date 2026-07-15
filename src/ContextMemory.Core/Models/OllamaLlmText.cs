namespace ContextMemory.Core.Models;

public static class OllamaLlmText
{
    public static string GetMessageContent(OllamaMessage? message)
    {
        if (message is null)
            return string.Empty;

        if (!string.IsNullOrWhiteSpace(message.Content))
            return message.Content;

        return message.Thinking ?? string.Empty;
    }

    public static string GetGenerateText(OllamaResponse? response)
    {
        if (response is null)
            return string.Empty;

        if (!string.IsNullOrWhiteSpace(response.Response))
            return response.Response;

        if (!string.IsNullOrWhiteSpace(response.Message?.Content))
            return response.Message.Content;

        if (!string.IsNullOrWhiteSpace(response.Thinking))
            return response.Thinking;

        return response.Message?.Thinking ?? string.Empty;
    }

    public static string NormalizeAssistantContent(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return raw;

        if (!raw.StartsWith("Thinking Process:", StringComparison.OrdinalIgnoreCase))
            return raw.Trim();

        const string outputMarker = "**Output:**";
        var outputIdx = raw.LastIndexOf(outputMarker, StringComparison.OrdinalIgnoreCase);
        if (outputIdx >= 0)
        {
            var after = raw[(outputIdx + outputMarker.Length)..].Trim().Trim('`', '"', '\'', ' ', '\r', '\n');
            if (after.Length > 0)
            {
                var line = after.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];
                return line.Trim('`', '*', ' ');
            }
        }

        var lines = raw.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i].Trim('`', '*', ' ');
            if (line.Length is > 0 and < 200
                && !line.StartsWith("**", StringComparison.Ordinal)
                && !line.EndsWith(":", StringComparison.Ordinal))
                return line;
        }

        return raw.Trim();
    }
}
