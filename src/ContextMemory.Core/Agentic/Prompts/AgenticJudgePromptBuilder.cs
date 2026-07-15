using System.Text;
using ContextMemory.Core.Localization;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic.Prompts;

public static class AgenticJudgePromptBuilder
{
    public static string Build(AgentValidationRequest request)
    {
        var lang = request.RuntimeConfig.DefaultLanguage;
        var profile = AgenticPromptProfileResolver.Resolve(request.RuntimeConfig);
        var objective = string.IsNullOrWhiteSpace(request.UserObjective)
            ? TenantLocale.Select(lang, "(not specified)", "(não especificado)")
            : request.UserObjective.Trim();

        var steps = FormatSteps(request.Steps, lang);
        var answer = request.FinalAnswer.Trim();
        var profileTag = profile switch
        {
            AgenticPromptProfile.OpenAi => "openai",
            AgenticPromptProfile.Claude => "claude",
            _ => "ollama"
        };

        var header = TenantLocale.Select(
            lang,
            $"[agentic-judge/{profileTag}] Evaluate whether the assistant's final answer satisfies the user objective.",
            $"[agentic-judge/{profileTag}] Avalia se a resposta final satisfaz o objetivo do utilizador.");

        var jsonRule = TenantLocale.Select(
            lang,
            "Respond ONLY with valid JSON: {\"valid\": boolean, \"feedback\": string}. No markdown.",
            "Responde APENAS com JSON válido: {\"valid\": boolean, \"feedback\": string}. Sem markdown.");

        var objectiveHeader = TenantLocale.Select(lang, "## User objective", "## Objetivo do utilizador");
        var stepsHeader = TenantLocale.Select(lang, "## Tool steps executed", "## Passos de tools executados");
        var answerHeader = TenantLocale.Select(lang, "## Proposed final answer", "## Resposta final proposta");
        var criteria = TenantLocale.Select(
            lang,
            """
            Criteria:
            - valid=true if the answer addresses the objective usefully and aligns with executed steps.
            - valid=false if it ignores the objective, invents facts, or is incomplete.
            - feedback must be short and actionable (only when valid=false).
            """,
            """
            Critérios:
            - valid=true se a resposta responde ao objetivo de forma útil e coerente com os passos executados.
            - valid=false se ignora o objetivo, inventa factos, ou é incompleta.
            - feedback curto e accionável (só quando valid=false).
            """);

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
            """;
    }

    private static string FormatSteps(IReadOnlyList<AgentExecutionStep> steps, string? language)
    {
        if (steps.Count == 0)
            return TenantLocale.Select(language, "(no tool steps executed)", "(nenhum passo de tool executado)");

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
