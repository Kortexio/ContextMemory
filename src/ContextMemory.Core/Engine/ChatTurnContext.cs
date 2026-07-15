using ContextMemory.Core.Agentic;
using ContextMemory.Core.Models;
using ContextMemory.Core.WebSearch;

namespace ContextMemory.Core.Engine;

public sealed class ChatTurnContext
{
    public WebSearchEnrichment? WebSearch { get; set; }

    public (OllamaRequest Request, OllamaMessage? LastUser, int PromptTokens)? Prepared { get; set; }

    public AgentResult? AgenticResult { get; set; }

    public bool AgenticUsageCharged { get; set; }

    public void Reset()
    {
        WebSearch = null;
        Prepared = null;
        AgenticResult = null;
        AgenticUsageCharged = false;
    }
}
