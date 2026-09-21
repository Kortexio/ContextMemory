using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

/// <summary>
/// Enforcement for <see cref="AgenticGuardrailKinds.ToolSurfaceHidden"/>.
/// Tool-name / intent markers and feedback come only from Admin <c>ConfigJson</c>
/// (<c>toolNameMarkers</c>, <c>intentPhrases</c>, <c>feedback</c>, optional <c>feedbackWithEvidence</c>, <c>feedbackMcp</c>).
/// </summary>
public static class AgenticToolIntentNarrationGuardrail
{
    public static bool TryGetRejectionFeedback(
        string finalAnswer,
        IReadOnlyList<AgentExecutionStep> steps,
        string configJson,
        AppRuntimeConfig runtimeConfig,
        out string feedback)
    {
        feedback = string.Empty;
        if (string.IsNullOrWhiteSpace(finalAnswer))
            return false;

        if (!runtimeConfig.Agentic.Enabled)
            return false;

        var toolNames = AgenticGuardrailConfigReader.GetStringList(configJson, "toolNameMarkers");
        var intentPhrases = AgenticGuardrailConfigReader.GetStringList(configJson, "intentPhrases");
        if (toolNames.Count == 0 && intentPhrases.Count == 0)
            return false;

        var hasMcp = HasConfiguredMcp(runtimeConfig);
        var hasNonDiscovery = HasSuccessfulNonDiscoveryTool(steps);
        var hasDiscovery = HasSuccessfulDiscoveryTool(steps);

        if (ContainsToolName(finalAnswer, toolNames))
        {
            feedback = BuildFeedback(configJson, runtimeConfig, hasMcp, hasNonDiscovery, hasDiscovery);
            return true;
        }

        if (hasNonDiscovery)
            return false;

        if (!LooksLikeToolIntentPhrase(finalAnswer, intentPhrases))
            return false;

        feedback = BuildFeedback(configJson, runtimeConfig, hasMcp, hasNonDiscovery: false, hasDiscovery);
        return true;
    }

    public static bool LooksLikeToolIntent(
        string finalAnswer,
        IReadOnlyList<string> toolNameMarkers,
        IReadOnlyList<string> intentPhrases) =>
        ContainsToolName(finalAnswer, toolNameMarkers)
        || LooksLikeToolIntentPhrase(finalAnswer, intentPhrases);

    public static bool ContainsToolName(string finalAnswer, IReadOnlyList<string> toolNameMarkers)
    {
        foreach (var tool in toolNameMarkers)
        {
            if (string.IsNullOrWhiteSpace(tool))
                continue;
            if (finalAnswer.Contains(tool, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string BuildFeedback(
        string configJson,
        AppRuntimeConfig runtimeConfig,
        bool hasMcp,
        bool hasNonDiscovery,
        bool hasDiscovery)
    {
        _ = hasDiscovery;
        if (hasNonDiscovery)
        {
            return AgenticGuardrailConfigReader.GetString(configJson, "feedbackWithEvidence")
                   ?? AgenticGuardrailConfigReader.ResolveFeedback(configJson, runtimeConfig.DefaultLanguage);
        }

        if (hasMcp)
        {
            return AgenticGuardrailConfigReader.GetString(configJson, "feedbackMcp")
                   ?? AgenticGuardrailConfigReader.ResolveFeedback(configJson, runtimeConfig.DefaultLanguage);
        }

        return AgenticGuardrailConfigReader.ResolveFeedback(configJson, runtimeConfig.DefaultLanguage);
    }

    private static bool LooksLikeToolIntentPhrase(string finalAnswer, IReadOnlyList<string> intentPhrases)
    {
        var text = finalAnswer.ToLowerInvariant();
        foreach (var phrase in intentPhrases)
        {
            if (string.IsNullOrWhiteSpace(phrase))
                continue;
            if (text.Contains(phrase.ToLowerInvariant(), StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static bool HasConfiguredMcp(AppRuntimeConfig runtimeConfig) =>
        runtimeConfig.Agentic.Tools.Integrations.Any(i =>
            string.Equals(i.Type, "mcp", StringComparison.OrdinalIgnoreCase)
            && i.Enabled
            && i.IsConfigured);

    private static bool HasSuccessfulDiscoveryTool(IReadOnlyList<AgentExecutionStep> steps)
    {
        foreach (var step in steps)
        {
            if (step.Success && SessionDiscoveryTools.IsDiscoveryTool(step.ToolName))
                return true;
        }

        return false;
    }

    private static bool HasSuccessfulNonDiscoveryTool(IReadOnlyList<AgentExecutionStep> steps)
    {
        foreach (var step in steps)
        {
            if (!step.Success)
                continue;
            if (SessionDiscoveryTools.IsDiscoveryTool(step.ToolName))
                continue;
            return true;
        }

        return false;
    }
}
