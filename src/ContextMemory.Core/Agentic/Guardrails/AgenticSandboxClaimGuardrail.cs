using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

/// <summary>
/// Rejects fabricated sandbox limitations when the tenant uses self-hosted-sandbox.
/// Markers and feedback come only from Admin <c>ConfigJson</c>
/// (<c>acaMarkers</c>, <c>noNetworkMarkers</c>, <c>sandboxSubjectMarkers</c>, <c>hypotheticalMarkers</c>, <c>feedback</c>).
/// Empty marker lists ⇒ no-op for that claim type.
/// </summary>
public static class AgenticSandboxClaimGuardrail
{
    public static bool HasSelfHostedSandbox(AppRuntimeConfig runtimeConfig) =>
        runtimeConfig.Agentic.Tools.Execution.Any(e =>
            string.Equals(e.Type, "self-hosted-sandbox", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(e.SandboxEndpoint));

    public static bool TryGetRejectionFeedback(
        string finalAnswer,
        IReadOnlyList<AgentExecutionStep> steps,
        string configJson,
        AppRuntimeConfig runtimeConfig,
        out string feedback)
    {
        feedback = string.Empty;
        if (!HasSelfHostedSandbox(runtimeConfig) || string.IsNullOrWhiteSpace(finalAnswer))
            return false;

        if (HasObservedSandboxNetworkFailure(steps))
            return false;

        var acaMarkers = AgenticGuardrailConfigReader.GetStringList(configJson, "acaMarkers");
        var noNetworkMarkers = AgenticGuardrailConfigReader.GetStringList(configJson, "noNetworkMarkers");
        var subjectMarkers = AgenticGuardrailConfigReader.GetStringList(configJson, "sandboxSubjectMarkers");
        var hypotheticalMarkers = AgenticGuardrailConfigReader.GetStringList(configJson, "hypotheticalMarkers");

        if (acaMarkers.Count == 0
            && noNetworkMarkers.Count == 0
            && hypotheticalMarkers.Count == 0)
        {
            return false;
        }

        var text = finalAnswer;
        var mentionsSandboxSubject = subjectMarkers.Any(m =>
            text.Contains(m, StringComparison.OrdinalIgnoreCase));

        var inventsAca = acaMarkers.Any(m => text.Contains(m, StringComparison.OrdinalIgnoreCase))
                         || (mentionsSandboxSubject
                             && text.Contains("aca", StringComparison.OrdinalIgnoreCase)
                             && (text.Contains("isolad", StringComparison.OrdinalIgnoreCase)
                                 || text.Contains("isolated", StringComparison.OrdinalIgnoreCase)));

        var inventsNoNetwork = mentionsSandboxSubject
                               && noNetworkMarkers.Any(m => text.Contains(m, StringComparison.OrdinalIgnoreCase));

        var inventsHypotheticalFailure =
            steps.Count == 0
            && mentionsSandboxSubject
            && hypotheticalMarkers.Any(m => text.Contains(m, StringComparison.OrdinalIgnoreCase));

        if (!inventsAca && !inventsNoNetwork && !inventsHypotheticalFailure)
            return false;

        feedback = AgenticGuardrailConfigReader.ResolveFeedback(configJson, runtimeConfig.DefaultLanguage);
        return true;
    }

    private static bool HasObservedSandboxNetworkFailure(IReadOnlyList<AgentExecutionStep> steps)
    {
        foreach (var step in steps)
        {
            if (step.Success)
                continue;

            if (!IsSandboxTool(step.ToolName))
                continue;

            var output = step.Output ?? string.Empty;
            if (output.Contains("timed out", StringComparison.OrdinalIgnoreCase)
                || output.Contains("timeout", StringComparison.OrdinalIgnoreCase)
                || output.Contains("NameResolution", StringComparison.OrdinalIgnoreCase)
                || output.Contains("nodename nor servname", StringComparison.OrdinalIgnoreCase)
                || output.Contains("Failed to establish", StringComparison.OrdinalIgnoreCase)
                || output.Contains("Connection refused", StringComparison.OrdinalIgnoreCase)
                || output.Contains("Network is unreachable", StringComparison.OrdinalIgnoreCase)
                || output.Contains("Temporary failure in name resolution", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSandboxTool(string toolName) =>
        string.Equals(toolName, AgenticToolRegistry.PythonExecuteToolName, StringComparison.OrdinalIgnoreCase)
        || string.Equals(toolName, AgenticToolRegistry.ShellExecuteToolName, StringComparison.OrdinalIgnoreCase)
        || string.Equals(toolName, AgenticToolRegistry.NodeExecuteToolName, StringComparison.OrdinalIgnoreCase);
}
