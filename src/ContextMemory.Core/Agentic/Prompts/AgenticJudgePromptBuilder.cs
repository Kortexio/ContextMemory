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
            - valid=false if the user asked about a URL/website and there is no tool step that fetched/searched that host, yet the answer describes the site.
            - feedback must be short and actionable (only when valid=false).
            """,
            """
            Critérios:
            - valid=true se a resposta responde ao objetivo de forma útil e coerente com os passos executados.
            - valid=false se ignora o objetivo, inventa factos, ou é incompleta.
            - valid=false se o utilizador perguntou sobre um URL/site e não há passo de tool que tenha ido buscar/pesquisado esse host, mas a resposta descreve o site.
            - feedback curto e accionável (só quando valid=false).
            """);

        var extra = BuildSoftCriteria(request.RuntimeConfig.ResolvedPolicy, lang);

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
        var pt = lang is not null && lang.StartsWith("pt", StringComparison.OrdinalIgnoreCase);

        void Add(string kind, string en, string ptText)
        {
            if (!policy.HasKind(kind))
                return;
            lines.Add("- " + (pt ? ptText : en));
            if (string.Equals(kind, AgenticGuardrailKinds.Readability, StringComparison.OrdinalIgnoreCase))
            {
                var target = AgenticGuardrailConfigReader.GetString(
                    policy.FindByKind(kind)?.ConfigJson ?? "{}",
                    "targetLevel") ?? "clear";
                lines.Add(pt
                    ? $"  (nível alvo de legibilidade: {target})"
                    : $"  (target readability level: {target})");
            }
        }

        Add(AgenticGuardrailKinds.LogicalFlow,
            "valid=false if reasoning is contradictory, jumps steps illogically, or conclusions do not follow from tool evidence.",
            "valid=false se o raciocínio for contraditório, saltar passos ou as conclusões não seguirem da evidência das tools.");
        Add(AgenticGuardrailKinds.ResponseQuality,
            "valid=false if the answer is low quality: vague, unhelpful, padded, or poorly structured for the objective.",
            "valid=false se a qualidade for baixa: vaga, pouco útil, com enchimento ou mal estruturada para o objetivo.");
        Add(AgenticGuardrailKinds.TranslationAccuracy,
            "If the objective asks for a translation, valid=false when the translation is inaccurate, incomplete, or wrong language.",
            "Se o objetivo pedir tradução, valid=false quando a tradução for imprecisa, incompleta ou no idioma errado.");
        Add(AgenticGuardrailKinds.Readability,
            "valid=false if the answer's complexity/tone does not match the configured readability target.",
            "valid=false se a complexidade/tom não corresponder ao nível de legibilidade configurado.");
        Add(AgenticGuardrailKinds.FactCheck,
            "valid=false if factual claims (statuses, amounts, IDs) are not supported by tool steps or contradict them.",
            "valid=false se claims factuais (estados, montantes, IDs) não forem suportados pelos passos de tools ou os contradisserem.");
        Add(AgenticGuardrailKinds.Relevance,
            "valid=false if the answer does not address the user objective.",
            "valid=false se a resposta não abordar o objetivo do utilizador.");
        Add(AgenticGuardrailKinds.PromptAddress,
            "valid=false if required items/keys from the user prompt are missing from the answer.",
            "valid=false se itens/chaves obrigatórios do pedido do utilizador faltarem na resposta.");

        if (lines.Count == 0)
            return string.Empty;

        var header = pt ? "## Critérios adicionais do tenant" : "## Additional tenant criteria";
        return header + Environment.NewLine + string.Join(Environment.NewLine, lines);
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
