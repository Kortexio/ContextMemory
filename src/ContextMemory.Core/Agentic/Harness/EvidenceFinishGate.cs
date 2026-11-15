using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic.Harness;

/// <summary>
/// Loop-facing finish gate: reuses live-data evidence rules and adds discovery-only refusal
/// (CodeForge diag-finish analogue). Does not replace the policy guardrail pipeline.
/// </summary>
public static class EvidenceFinishGate
{
    public sealed record Decision(bool Refuse, string? FeedbackForModel, string Reason);

    public static Decision Evaluate(
        string? userObjective,
        string? finalAnswer,
        IReadOnlyList<AgentExecutionStep> steps,
        AppRuntimeConfig runtimeConfig,
        ModelHarnessProfile profile,
        int finishNudgesAlready)
    {
        if (!profile.EnableEvidenceFinishGate)
            return new Decision(false, null, "disabled");

        if (finishNudgesAlready >= profile.MaxFinishNudges)
            return new Decision(false, null, "nudge-cap");

        if (string.IsNullOrWhiteSpace(finalAnswer))
            return new Decision(false, null, "empty-deferred-to-validator");

        // Live-data: reuse existing guardrail markers via policy pack.
        var policy = runtimeConfig.ResolvedPolicy;
        if (policy.HasKind(AgenticGuardrailKinds.LiveDataEvidence))
        {
            var pack = policy.FindByKind(AgenticGuardrailKinds.LiveDataEvidence);
            var configJson = pack?.ConfigJson ?? "{}";
            if (AgenticLiveDataEvidenceGuardrail.TryGetRejectionFeedback(
                    userObjective,
                    finalAnswer,
                    steps,
                    configJson,
                    runtimeConfig,
                    out var liveFeedback))
            {
                return new Decision(
                    true,
                    liveFeedback,
                    "live-data-without-evidence");
            }
        }

        if (IsDiscoveryOnlyFinish(steps))
        {
            return new Decision(
                true,
                "Discovery-only tools ran (tool_describe/skill_*/tool_search/…). "
                + "That is not a finished answer when the user needs facts or actions. "
                + "Call wiki_search, an MCP tool, http, or sandbox next — then answer.",
                "discovery-only");
        }

        return new Decision(false, null, "ok");
    }

    public static bool IsDiscoveryOnlyFinish(IReadOnlyList<AgentExecutionStep> steps)
    {
        if (steps.Count == 0)
            return false;

        var anySuccess = steps.Any(s => s.Success);
        if (!anySuccess)
            return false;

        var successful = steps.Where(s => s.Success).ToList();
        if (successful.Count == 0)
            return false;

        // All successful tools were discovery harness helpers — no real evidence/effect.
        return successful.All(s => SessionDiscoveryTools.IsDiscoveryTool(s.ToolName));
    }
}
