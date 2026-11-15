using System.Text.RegularExpressions;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

/// <summary>
/// Strong identifiers in the answer must appear in successful tool outputs (source-context / fact-check grounding).
/// </summary>
public static partial class AgenticSourceGroundingGuardrail
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

        var evidence = BuildEvidenceBlob(steps);
        if (string.IsNullOrWhiteSpace(evidence))
        {
            // No tool evidence yet — do not invent IDs; only reject if answer contains strong IDs.
            var idsNoEvidence = ExtractStrongIds(finalAnswer);
            if (idsNoEvidence.Count == 0)
                return false;

            feedback = AgenticGuardrailConfigReader.ResolveFeedback(configJson, runtimeConfig.DefaultLanguage);
            return true;
        }

        foreach (var id in ExtractStrongIds(finalAnswer))
        {
            if (!evidence.Contains(id, StringComparison.OrdinalIgnoreCase))
            {
                feedback = AgenticGuardrailConfigReader.ResolveFeedback(configJson, runtimeConfig.DefaultLanguage);
                return true;
            }
        }

        return false;
    }

    public static IReadOnlyList<string> ExtractStrongIds(string text)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in IssueKeyRegex().Matches(text))
            set.Add(m.Value);
        foreach (Match m in AccountLikeRegex().Matches(text))
            set.Add(m.Value);
        return set.ToList();
    }

    private static string BuildEvidenceBlob(IReadOnlyList<AgentExecutionStep> steps)
    {
        var parts = steps
            .Where(s => s.Success && !SessionDiscoveryTools.IsDiscoveryTool(s.ToolName))
            .Select(s => s.Output ?? string.Empty);
        return string.Join('\n', parts);
    }

    [GeneratedRegex(@"\b[A-Z][A-Z0-9]{1,9}-\d+\b", RegexOptions.CultureInvariant)]
    private static partial Regex IssueKeyRegex();

    [GeneratedRegex(@"\bA\d{5,}\b", RegexOptions.CultureInvariant)]
    private static partial Regex AccountLikeRegex();
}
