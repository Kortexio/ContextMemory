using System.Text.RegularExpressions;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

/// <summary>
/// Rejects live-data answers without successful MCP/wiki evidence.
/// Markers and feedback come only from Admin <c>ConfigJson</c>
/// (<c>liveDataMarkers</c>, <c>evidenceToolMarkers</c>, <c>honestUnknownMarkers</c>, <c>feedback</c>).
/// </summary>
public static partial class AgenticLiveDataEvidenceGuardrail
{
    public static bool TryGetRejectionFeedback(
        string? userObjective,
        string finalAnswer,
        IReadOnlyList<AgentExecutionStep> steps,
        string configJson,
        AppRuntimeConfig runtimeConfig,
        out string feedback)
    {
        feedback = string.Empty;
        if (string.IsNullOrWhiteSpace(finalAnswer))
            return false;

        if (!IsLiveDataQuestion(userObjective, configJson, runtimeConfig))
            return false;

        var evidenceMarkers = AgenticGuardrailConfigReader.GetStringList(configJson, "evidenceToolMarkers");
        if (HasSuccessfulEvidence(steps, evidenceMarkers))
            return false;

        var honestMarkers = AgenticGuardrailConfigReader.GetStringList(configJson, "honestUnknownMarkers");
        if (HasEvidenceAttempt(steps, evidenceMarkers) && LooksLikeHonestUnknown(finalAnswer, honestMarkers))
            return false;

        feedback = AgenticGuardrailConfigReader.ResolveFeedback(configJson, runtimeConfig.DefaultLanguage);
        return true;
    }

    public static bool IsLiveDataQuestion(
        string? userObjective,
        string configJson,
        AppRuntimeConfig runtimeConfig)
    {
        if (!HasEvidenceBackend(runtimeConfig))
            return false;

        if (string.IsNullOrWhiteSpace(userObjective))
            return false;

        if (IssueKeyRegex().IsMatch(userObjective))
            return true;

        var markers = AgenticGuardrailConfigReader.GetStringList(configJson, "liveDataMarkers");
        if (markers.Count == 0)
            return false;

        var text = userObjective.ToLowerInvariant();
        foreach (var marker in markers)
        {
            if (string.IsNullOrWhiteSpace(marker))
                continue;
            if (text.Contains(marker.ToLowerInvariant(), StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static bool HasEvidenceBackend(AppRuntimeConfig runtimeConfig)
    {
        if (runtimeConfig.GlobalWikiEnabled)
            return true;

        return runtimeConfig.Agentic.Tools.Integrations.Any(i =>
            i.Enabled
            && string.Equals(i.Type, "mcp", StringComparison.OrdinalIgnoreCase)
            && i.IsConfigured);
    }

    private static bool HasSuccessfulEvidence(
        IReadOnlyList<AgentExecutionStep> steps,
        IReadOnlyList<string> evidenceMarkers) =>
        steps.Any(s => s.Success && IsEvidenceTool(s.ToolName, evidenceMarkers));

    private static bool HasEvidenceAttempt(
        IReadOnlyList<AgentExecutionStep> steps,
        IReadOnlyList<string> evidenceMarkers) =>
        steps.Any(s => IsEvidenceTool(s.ToolName, evidenceMarkers));

    private static bool IsEvidenceTool(string? toolName, IReadOnlyList<string> evidenceMarkers)
    {
        if (string.IsNullOrWhiteSpace(toolName) || SessionDiscoveryTools.IsDiscoveryTool(toolName))
            return false;

        foreach (var marker in evidenceMarkers)
        {
            if (string.IsNullOrWhiteSpace(marker))
                continue;
            if (toolName.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return toolName.Contains("__", StringComparison.Ordinal);
    }

    private static bool LooksLikeHonestUnknown(string finalAnswer, IReadOnlyList<string> markers)
    {
        foreach (var marker in markers)
        {
            if (string.IsNullOrWhiteSpace(marker))
                continue;
            if (finalAnswer.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    [GeneratedRegex(@"\b[A-Z][A-Z0-9]{1,9}-\d+\b", RegexOptions.CultureInvariant)]
    private static partial Regex IssueKeyRegex();
}
