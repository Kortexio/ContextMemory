namespace ContextMemory.Core.Models;

public static class OllamaMessageExtensions
{
    public static OllamaMessage? GetLastUserMessage(this IReadOnlyList<OllamaMessage> messages) =>
        messages.LastOrDefault(m => string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase));

    public static OllamaMessage? GetLastUserMessage(this OllamaRequest request) =>
        request.Messages.GetLastUserMessage();
}
