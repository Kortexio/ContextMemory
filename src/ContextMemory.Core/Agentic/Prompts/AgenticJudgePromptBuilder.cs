using System.Text;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic.Prompts;

public static class AgenticJudgePromptBuilder
{
    public static string Build(AgentValidationRequest request)
    {
        var profile = AgenticPromptProfileResolver.Resolve(request.RuntimeConfig);
        var objective = string.IsNullOrWhiteSpace(request.UserObjective)
            ? "(not specified)"
            : request.UserObjective.Trim();

        var steps = FormatSteps(request.Steps, request.RuntimeConfig.DefaultLanguage);
        var answer = request.FinalAnswer.Trim();
        var profileTag = profile switch
        {
            AgenticPromptProfile.OpenAi => "openai",
            AgenticPromptProfile.Claude => "claude",
            _ => "ollama"
        };

        var header =
            $"[agentic-judge/{profileTag}] Evaluate whether the assistant's final answer satisfies the user objective.";

        var jsonRule =
            "Respond ONLY with valid JSON: {\"valid\": boolean, \"feedback\": string}. No markdown.";

        var objectiveHeader = "## User objective";
        var stepsHeader = "## Tool steps executed";
        var answerHeader = "## Proposed final answer";
        var criteria = """
            Criteria:
            - valid=true if the answer addresses the objective usefully and aligns with executed steps.
            - valid=false if it ignores the objective, invents facts, or is incomplete.
            - valid=false if the user asked about a URL/website and there is no tool step that fetched/searched that host, yet the answer describes the site.
            - feedback must be short and actionable (only when valid=false).
            """;

        var extra = BuildSoftCriteria(request.RuntimeConfig.ResolvedPolicy, request.RuntimeConfig.DefaultLanguage);

        return $"""
            {header}

            {jsonRule}

            {objectiveHeader}
            {objective}

            {stepsHeader}
            {steps}

            {answerHeader}
            {answer}

            {criteria}
            {extra}
            """;
    }

    private static string BuildSoftCriteria(ResolvedAgenticPolicy policy, string? lang)
    {
        var lines = new List<string>();

        void Add(string kind, string en)
        {
            if (!policy.HasKind(kind))
                return;
            lines.Add("- " + en);
            if (string.Equals(kind, AgenticGuardrailKinds.Readability, StringComparison.OrdinalIgnoreCase))
            {
                var target = AgenticGuardrailConfigReader.GetString(
                    policy.FindByKind(kind)?.ConfigJson ?? "{}",
                    "targetLevel") ?? "clear";
                lines.Add($"  (target readability level: {target})");
            }
        }

        Add(AgenticGuardrailKinds.LogicalFlow,
            "valid=false if reasoning is contradictory, jumps steps illogically, or conclusions do not follow from tool evidence.");
        Add(AgenticGuardrailKinds.ResponseQuality,
            "valid=false if the answer is low quality: vague, unhelpful, padded, or poorly structured for the objective.");
        Add(AgenticGuardrailKinds.TranslationAccuracy,
            "If the objective asks for a translation, valid=false when the translation is inaccurate, incomplete, or wrong language.");
        Add(AgenticGuardrailKinds.Readability,
            "valid=false if the answer's complexity/tone does not match the configured readability target.");
        Add(AgenticGuardrailKinds.FactCheck,
            "valid=false if factual claims (statuses, amounts, IDs) are not supported by tool steps or contradict them.");
        Add(AgenticGuardrailKinds.Relevance,
            "valid=false if the answer does not address the user objective.");
        Add(AgenticGuardrailKinds.PromptAddress,
            "valid=false if required items/keys from the user prompt are missing from the answer.");

        if (lines.Count == 0)
            return string.Empty;

        return "## Additional tenant criteria" + Environment.NewLine + string.Join(Environment.NewLine, lines);
    }

    private static string FormatSteps(IReadOnlyList<AgentExecutionStep> steps, string? language)
    {
        if (steps.Count == 0)
            return "(no tool steps executed)";

        var sb = new StringBuilder();
        foreach (var step in steps)
        {
            sb.AppendLine(
                $"- {step.ToolName} (exit={step.ExitCode ?? 0}): {Truncate(step.Output, 400)}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";
}
