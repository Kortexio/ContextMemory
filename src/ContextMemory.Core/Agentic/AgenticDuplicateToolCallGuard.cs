using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ContextMemory.Core.Localization;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

/// <summary>
/// Blocks empty / identical wiki_search (and similar) loops so weak models cannot spin
/// on <c>{}</c> or the same query forever — whether the prior call succeeded or failed.
/// Non-wiki tools still only block after an identical <em>successful</em> call, except
/// deterministic session-discovery retries, which are blocked after the first identical failure.
/// </summary>
public static class AgenticDuplicateToolCallGuard
{
    private static readonly Regex UnicodeEscape = new(
        @"\\u([0-9a-fA-F]{4})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> QueryFocusedTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "wiki_search",
        "wiki_grep"
    };

    /// <summary>Max wiki_search/wiki_grep attempts (success or fail) before forcing a pivot.</summary>
    public const int MaxWikiAttemptsPerTurn = 2;

    /// <summary>
    /// After this many consecutive wiki-budget rejections, strip tools and force a final answer.
    /// </summary>
    public const int MaxWikiBudgetRejectionsBeforeForceAnswer = 2;

    /// <summary>
    /// After this many rejections of an identical tool+args that already succeeded, force a final answer
    /// from the evidence already in the turn (do not burn iterations on stubborn repeats).
    /// </summary>
    public const int MaxDuplicateAfterSuccessRejectionsBeforeForceAnswer = 1;

    /// <summary>Step summary when an identical successful tool+args is rejected.</summary>
    public const string DuplicateAfterSuccessSummary = "Duplicate after success rejected";

    /// <summary>Generic duplicate rejection summary (empty wiki query, wiki identical, etc.).</summary>
    public const string DuplicateRejectedSummary = "Duplicate tool call rejected";

    /// <summary>Identical failed tool call rejected after its bounded retry allowance.</summary>
    public const string DuplicateAfterFailureSummary = "Duplicate after failure rejected";

    /// <summary>
    /// True when feedback from <see cref="TryReject"/> means the identical call already succeeded.
    /// </summary>
    public static bool FeedbackIndicatesDuplicateAfterSuccess(string? feedback)
    {
        if (string.IsNullOrWhiteSpace(feedback))
            return false;
        return feedback.Contains("already succeeded", StringComparison.OrdinalIgnoreCase)
               || feedback.Contains("já teve sucesso", StringComparison.OrdinalIgnoreCase);
    }

    public static bool FeedbackIndicatesDuplicateAfterFailure(string? feedback)
    {
        if (string.IsNullOrWhiteSpace(feedback))
            return false;
        return feedback.Contains("already failed", StringComparison.OrdinalIgnoreCase)
               || feedback.Contains("já falhou", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryReject(
        string toolName,
        string? argumentsJson,
        IReadOnlyList<AgentExecutionStep> steps,
        AppRuntimeConfig runtimeConfig,
        out string feedback)
    {
        feedback = string.Empty;
        if (string.IsNullOrWhiteSpace(toolName))
            return false;

        var name = NormalizeToolName(toolName);
        if (QueryFocusedTools.Contains(name))
        {
            var wikiAttempts = steps.Count(s => QueryFocusedTools.Contains(NormalizeToolName(s.ToolName)));
            if (wikiAttempts >= MaxWikiAttemptsPerTurn)
            {
                feedback = BuildWikiBudgetFeedback(steps, runtimeConfig);
                return true;
            }

            var query = ExtractQuery(argumentsJson);
            if (string.IsNullOrWhiteSpace(query))
            {
                feedback = BuildEmptyQueryFeedback(name, runtimeConfig);
                return true;
            }
        }

        if (steps.Count == 0)
            return false;

        var signature = BuildSignature(toolName, argumentsJson);
        if (string.IsNullOrEmpty(signature))
            return false;

        var sameSignature = steps.Where(s =>
            string.Equals(NormalizeToolName(s.ToolName), name, StringComparison.Ordinal)
            && string.Equals(BuildSignature(s.ToolName, s.Arguments), signature, StringComparison.Ordinal))
            .ToList();

        if (QueryFocusedTools.Contains(name))
        {
            // Empty/identical wiki retries never help — block after any prior attempt.
            if (sameSignature.Count == 0)
                return false;

            feedback = BuildFeedback(toolName, runtimeConfig, afterFailure: sameSignature.All(s => !s.Success));
            return true;
        }

        // Discovery calls are local, deterministic reads/searches. Repeating the exact failed
        // arguments cannot recover and previously burned every remaining iteration (for example
        // artifact_read with a malformed field name).
        if (SessionDiscoveryTools.IsDiscoveryTool(name)
            && sameSignature.Count > 0
            && sameSignature.All(s => !s.Success))
        {
            feedback = BuildFeedback(toolName, runtimeConfig, afterFailure: true);
            return true;
        }

        // Real MCP/sandbox/HTTP failures may be transient, so allow one identical retry.
        // A third identical execution is almost certainly a model loop and must be cut.
        if (sameSignature.Count >= 2 && sameSignature.All(s => !s.Success))
        {
            feedback = BuildFeedback(toolName, runtimeConfig, afterFailure: true);
            return true;
        }

        var alreadySucceeded = sameSignature.Any(s => s.Success);
        if (!alreadySucceeded)
            return false;

        feedback = BuildFeedback(toolName, runtimeConfig, afterFailure: false);
        return true;
    }

    public static string BuildSignature(string toolName, string? argumentsJson)
    {
        var name = NormalizeToolName(toolName);
        if (QueryFocusedTools.Contains(name))
        {
            var query = ExtractQuery(argumentsJson);
            return name + "|q=" + NormalizeText(query);
        }

        return name + "|a=" + NormalizeArguments(argumentsJson);
    }

    private static string NormalizeArguments(string? argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
            return string.Empty;

        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            var builder = new StringBuilder(argumentsJson.Length);
            AppendCanonicalJson(doc.RootElement, builder);
            return builder.ToString();
        }
        catch (JsonException)
        {
            return NormalizeText(argumentsJson);
        }
    }

    private static void AppendCanonicalJson(JsonElement element, StringBuilder builder)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                builder.Append('{');
                var firstProperty = true;
                foreach (var property in element.EnumerateObject()
                             .OrderBy(p => NormalizeArgumentName(p.Name), StringComparer.Ordinal))
                {
                    if (!firstProperty)
                        builder.Append(',');
                    firstProperty = false;
                    builder.Append(JsonSerializer.Serialize(NormalizeArgumentName(property.Name)));
                    builder.Append(':');
                    AppendCanonicalJson(property.Value, builder);
                }
                builder.Append('}');
                break;

            case JsonValueKind.Array:
                builder.Append('[');
                var firstItem = true;
                foreach (var item in element.EnumerateArray())
                {
                    if (!firstItem)
                        builder.Append(',');
                    firstItem = false;
                    AppendCanonicalJson(item, builder);
                }
                builder.Append(']');
                break;

            case JsonValueKind.String:
                builder.Append(JsonSerializer.Serialize(NormalizeText(element.GetString())));
                break;

            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Null:
                builder.Append(element.GetRawText().ToLowerInvariant());
                break;

            default:
                builder.Append(element.GetRawText());
                break;
        }
    }

    private static string NormalizeArgumentName(string name) =>
        name.Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

    internal static string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var unescaped = UnicodeEscape.Replace(value, m =>
        {
            var code = int.Parse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return char.ConvertFromUtf32(code);
        });

        var sb = new StringBuilder(unescaped.Length);
        var prevSpace = false;
        foreach (var ch in unescaped.Trim().ToLowerInvariant())
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!prevSpace)
                    sb.Append(' ');
                prevSpace = true;
            }
            else
            {
                sb.Append(ch);
                prevSpace = false;
            }
        }

        return sb.ToString();
    }

    private static string NormalizeToolName(string toolName) =>
        toolName.Trim().ToLowerInvariant();

    private static string ExtractQuery(string? argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
            return string.Empty;

        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return string.Empty;

            // wiki_search uses "query"; wiki_grep uses "pattern".
            if (doc.RootElement.TryGetProperty("query", out var q)
                && q.ValueKind == JsonValueKind.String)
            {
                return q.GetString() ?? string.Empty;
            }

            if (doc.RootElement.TryGetProperty("pattern", out var p)
                && p.ValueKind == JsonValueKind.String)
            {
                return p.GetString() ?? string.Empty;
            }

            // {} or object without query/pattern → empty (do not fall back to raw JSON).
            return string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// True when the model keeps hitting the wiki budget (with or without prior wiki evidence).
    /// Caller should strip tools and force a text answer — otherwise weak models burn all iterations
    /// on repeated budget rejections (even when feedback asked for MCP).
    /// </summary>
    public static bool ShouldForceAnswerAfterWikiBudget(IReadOnlyList<AgentExecutionStep> steps)
    {
        var trailing = 0;
        for (var i = steps.Count - 1; i >= 0; i--)
        {
            if (!IsWikiBudgetRejection(steps[i]))
                break;
            trailing++;
        }

        return trailing >= MaxWikiBudgetRejectionsBeforeForceAnswer;
    }

    /// <summary>
    /// True when an identical tool+args that already succeeded was rejected again.
    /// Caller should strip tools and answer from the successful result already in the turn.
    /// </summary>
    public static bool ShouldForceAnswerAfterDuplicateSuccess(IReadOnlyList<AgentExecutionStep> steps)
    {
        var trailing = 0;
        for (var i = steps.Count - 1; i >= 0; i--)
        {
            if (!IsDuplicateAfterSuccessRejection(steps[i]))
                break;
            trailing++;
        }

        return trailing >= MaxDuplicateAfterSuccessRejectionsBeforeForceAnswer;
    }

    /// <summary>
    /// A deterministic discovery call failed and the model immediately repeated the exact call.
    /// If some evidence already succeeded this turn, stop tooling and answer from that evidence.
    /// </summary>
    public static bool ShouldForceAnswerAfterRepeatedToolFailure(
        IReadOnlyList<AgentExecutionStep> steps)
    {
        if (steps.Count == 0)
            return false;

        var last = steps[^1];
        return !last.Success
               && string.Equals(last.Summary, DuplicateAfterFailureSummary, StringComparison.Ordinal);
    }

    public static bool ShouldForceAnswer(IReadOnlyList<AgentExecutionStep> steps) =>
        ShouldForceAnswerAfterWikiBudget(steps)
        || ShouldForceAnswerAfterDuplicateSuccess(steps)
        || ShouldForceAnswerAfterRepeatedToolFailure(steps);

    /// <summary>
    /// After tools were stripped for force-answer, accept a non-empty model reply when successful
    /// tool evidence already exists — even if soft validators reject — but never accept
    /// guardrail/budget mechanics echoed to the end user, or answers that leak tool names.
    /// </summary>
    public static bool ShouldAcceptForceAnswerDespiteValidation(
        bool forceAnswerOnly,
        string? finalAnswer,
        IReadOnlyList<AgentExecutionStep> steps) =>
        forceAnswerOnly
        && IsUsableForceAnswer(finalAnswer)
        && steps.Any(s => s.Success);

    /// <summary>
    /// True when the model paraphrases harness rejections (budget, duplicate calls, "how to fix")
    /// instead of answering the user's original question.
    /// </summary>
    public static bool IsGuardrailMechanicsEcho(string? finalAnswer)
    {
        if (string.IsNullOrWhiteSpace(finalAnswer))
            return false;

        foreach (var marker in GuardrailMechanicsEchoMarkers)
        {
            if (finalAnswer.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static bool IsUsableForceAnswer(string? finalAnswer) =>
        !string.IsNullOrWhiteSpace(finalAnswer)
        && !IsGuardrailMechanicsEcho(finalAnswer)
        && !AgenticToolIntentNarrationGuardrail.ContainsToolName(finalAnswer)
        && !AgenticDuplicateSentenceGuardrail.ContainsDuplicateContent(finalAnswer);

    /// <summary>
    /// Last-resort user-visible reply from successful tool outputs when the model only
    /// echoes budget/duplicate mechanics after force-answer.
    /// </summary>
    public static string? TryBuildEvidenceFallbackAnswer(
        AppRuntimeConfig runtimeConfig,
        IReadOnlyList<AgentExecutionStep> steps,
        int maxTotalChars = 6000)
    {
        var chunks = steps
            .Where(s => s.Success && !string.IsNullOrWhiteSpace(s.Output))
            .Select(s => TruncateForFallback(s.Output.Trim(), 2500))
            .Where(o => o.Length > 0)
            .TakeLast(4)
            .ToList();

        if (chunks.Count == 0)
            return null;

        var intro = TenantLocale.Select(
            runtimeConfig.DefaultLanguage,
            "Here is what was found:",
            "Segue o que foi encontrado:");

        var combined = intro + "\n\n" + string.Join("\n\n---\n\n", chunks);
        return combined.Length <= maxTotalChars
            ? combined
            : combined[..maxTotalChars] + "…";
    }

    /// <summary>
    /// Honest terminal response when tooling has been disabled after repeated real failures and
    /// no successful evidence exists. Avoids burning text-only iterations against evidence guards.
    /// </summary>
    public static string BuildFailureFallbackAnswer(
        AppRuntimeConfig runtimeConfig,
        IReadOnlyList<AgentExecutionStep> steps)
    {
        var lastRealFailure = steps.LastOrDefault(s =>
            !s.Success
            && !IsHarnessPolicyRejection(s)
            && !string.IsNullOrWhiteSpace(s.Output));
        var detail = lastRealFailure is null
            ? string.Empty
            : " " + TruncateForFallback(lastRealFailure.Output.Trim(), 1000);

        return TenantLocale.Select(
                   runtimeConfig.DefaultLanguage,
                   "I could not obtain the requested data after repeated attempts. Last error:",
                   "Não foi possível obter os dados pedidos após tentativas repetidas. Último erro:")
               + detail;
    }

    private static string TruncateForFallback(string text, int maxChars) =>
        text.Length <= maxChars ? text : text[..maxChars] + "…";

    private static readonly string[] GuardrailMechanicsEchoMarkers =
    [
        "budget exhausted",
        "wiki budget",
        "orçamento wiki",
        "orçamento de chamadas",
        "limite de orçamento",
        "mesmo parâmetro",
        "mesmos arguments",
        "mesmos argumentos",
        "same parameter",
        "same arguments",
        "não chamar a mesma",
        "do not call the same",
        "não repitas",
        "do not repeat",
        "rejeitada pelo sistema",
        "rejeitado pelo sistema",
        "rejected by the system",
        "como corrigir",
        "how to fix",
        "how to correct",
        "consecutivamente",
        "consecutively",
        "ferramenta foi chamada mais",
        "tool was called more",
        "identical tool call",
        "chamada idêntica",
        "duplicate tool",
        "tool call already succeeded"
    ];

    public static bool IsDuplicateAfterSuccessRejection(AgentExecutionStep step)
    {
        if (step.Success)
            return false;

        if (string.Equals(step.Summary, DuplicateAfterSuccessSummary, StringComparison.Ordinal))
            return true;

        return FeedbackIndicatesDuplicateAfterSuccess(step.Output);
    }

    /// <summary>
    /// Harness-only rejections (duplicate args, wiki budget, empty query) — not real tool failures.
    /// Must not trip RequireZeroExitCode / tool-failure-disclosure or the model is pushed to
    /// explain budget mechanics to the end user.
    /// </summary>
    public static bool IsHarnessPolicyRejection(AgentExecutionStep step)
    {
        if (step.Success)
            return false;

        if (string.Equals(step.Summary, DuplicateAfterSuccessSummary, StringComparison.Ordinal)
            || string.Equals(step.Summary, DuplicateRejectedSummary, StringComparison.Ordinal)
            || string.Equals(step.Summary, DuplicateAfterFailureSummary, StringComparison.Ordinal))
            return true;

        return IsWikiBudgetRejection(step) || IsDuplicateAfterSuccessRejection(step);
    }

    public static string BuildForceAnswerNudge(AppRuntimeConfig runtimeConfig, IReadOnlyList<AgentExecutionStep> steps)
    {
        var antiMeta = TenantLocale.Select(
            runtimeConfig.DefaultLanguage,
            " CRITICAL: Answer ONLY the user's original question with facts from tool results. "
            + "Do NOT explain budgets, duplicate calls, rejections, or how to call tools. "
            + "Do NOT name tools (wiki_search, wiki_grep, …).",
            " CRÍTICO: Responde APENAS à pergunta original do utilizador com factos dos resultados das tools. "
            + "NÃO expliques orçamentos, chamadas duplicadas, rejeições, nem como chamar tools. "
            + "NÃO nomes tools (wiki_search, wiki_grep, …).");

        if (ShouldForceAnswerAfterDuplicateSuccess(steps))
        {
            return TenantLocale.Select(
                runtimeConfig.DefaultLanguage,
                "STOP. That exact tool call already succeeded earlier this turn. "
                + "Answer the user NOW from the tool result already gathered. "
                + "Do NOT emit tool_calls or JSON tool invocations.",
                "PARA. Essa chamada exacta de tool já teve sucesso neste turno. "
                + "Responde AGORA ao utilizador com o resultado da tool já obtido. "
                + "NÃO emitas tool_calls nem invocações JSON de tools.")
                + antiMeta;
        }

        if (ShouldForceAnswerAfterRepeatedToolFailure(steps))
        {
            return TenantLocale.Select(
                runtimeConfig.DefaultLanguage,
                "STOP. The same tool call failed repeatedly and was blocked. "
                + "Answer the user's original question NOW from evidence already gathered, "
                + "or explain honestly that the requested data could not be obtained. "
                + "Do NOT emit tool_calls or JSON tool invocations.",
                "PARA. A mesma chamada de tool falhou repetidamente e foi bloqueada. "
                + "Responde AGORA à pergunta original com a evidência já recolhida "
                + "ou explica honestamente que não foi possível obter os dados pedidos. "
                + "NÃO emitas tool_calls nem invocações JSON de tools.")
                + antiMeta;
        }

        if (HasSuccessfulWikiEvidence(steps))
        {
            return TenantLocale.Select(
                runtimeConfig.DefaultLanguage,
                "STOP. Wiki budget is exhausted and evidence was already gathered. "
                + "Answer the user NOW in plain text. Do NOT emit tool_calls or JSON tool invocations.",
                "PARA. O orçamento wiki esgotou-se e já há evidência recolhida. "
                + "Responde AGORA ao utilizador em texto. NÃO emitas tool_calls nem invocações JSON de tools.")
                + antiMeta;
        }

        return TenantLocale.Select(
            runtimeConfig.DefaultLanguage,
            "STOP. Wiki budget is exhausted and further wiki calls are blocked. "
            + "Answer the user NOW honestly from what you have (or say you could not get live data). "
            + "Do NOT emit tool_calls or JSON tool invocations.",
            "PARA. O orçamento wiki esgotou-se e novas chamadas wiki estão bloqueadas. "
            + "Responde AGORA com honestidade com o que tens (ou diz que não obtiveste dados vivos). "
            + "NÃO emitas tool_calls nem invocações JSON de tools.")
            + antiMeta;
    }

    public static bool IsWikiBudgetRejection(AgentExecutionStep step)
    {
        if (step.Success || string.IsNullOrWhiteSpace(step.ToolName))
            return false;
        if (!QueryFocusedTools.Contains(NormalizeToolName(step.ToolName)))
            return false;

        var output = step.Output ?? string.Empty;
        return output.Contains("budget exhausted", StringComparison.OrdinalIgnoreCase)
               || output.Contains("orçamento", StringComparison.OrdinalIgnoreCase)
               || output.Contains("esgotado", StringComparison.OrdinalIgnoreCase);
    }

    public static bool HasSuccessfulWikiEvidence(IReadOnlyList<AgentExecutionStep> steps) =>
        steps.Any(s => s.Success && QueryFocusedTools.Contains(NormalizeToolName(s.ToolName)));

    private static string BuildWikiBudgetFeedback(
        IReadOnlyList<AgentExecutionStep> steps,
        AppRuntimeConfig runtimeConfig)
    {
        var lang = runtimeConfig.DefaultLanguage;

        // When wiki already succeeded, pushing MCP makes weak models loop on more tools
        // instead of answering from the evidence they already have.
        if (HasSuccessfulWikiEvidence(steps))
        {
            return TenantLocale.Select(
                lang,
                "Rejected: wiki_search/wiki_grep budget exhausted this turn. "
                + "Do NOT call any tool. Answer the user NOW from the wiki evidence already gathered. "
                + "No tool_calls, no JSON tool invocations.",
                "Rejeitado: orçamento wiki_search/wiki_grep esgotado neste turno. "
                + "NÃO chames nenhuma tool. Responde AGORA ao utilizador com a evidência wiki já recolhida. "
                + "Sem tool_calls, sem invocações JSON de tools.");
        }

        var hasMcp = HasConfiguredMcp(runtimeConfig);
        if (hasMcp)
        {
            return TenantLocale.Select(
                lang,
                "Rejected: wiki_search/wiki_grep budget exhausted this turn. "
                + "Do NOT call wiki again. Call a configured MCP tool now as ONLY JSON "
                + "{\"tool\":\"server__tool\",\"arguments\":{...}} "
                + "(e.g. …__ask_zuora, …__query_objects).",
                "Rejeitado: orçamento wiki_search/wiki_grep esgotado neste turno. "
                + "NÃO chames wiki outra vez. Chama agora uma tool MCP como APENAS JSON "
                + "{\"tool\":\"server__tool\",\"arguments\":{...}} "
                + "(ex. …__ask_zuora, …__query_objects).");
        }

        return TenantLocale.Select(
            lang,
            "Rejected: wiki_search/wiki_grep budget exhausted this turn. "
            + "Answer from evidence already gathered or change approach — do not call wiki again.",
            "Rejeitado: orçamento wiki_search/wiki_grep esgotado neste turno. "
            + "Responde com a evidência já recolhida ou muda de abordagem — não chames wiki outra vez.");
    }

    private static string BuildEmptyQueryFeedback(string toolName, AppRuntimeConfig runtimeConfig)
    {
        var lang = runtimeConfig.DefaultLanguage;
        var hasMcp = HasConfiguredMcp(runtimeConfig);
        var field = string.Equals(toolName, "wiki_grep", StringComparison.Ordinal) ? "pattern" : "query";

        if (hasMcp)
        {
            return TenantLocale.Select(
                lang,
                $"Rejected: `{toolName}` needs a non-empty \"{field}\". "
                + $"Retry as ONLY JSON {{\"tool\":\"{toolName}\",\"arguments\":{{\"{field}\":\"concrete keywords\"}}}} "
                + "OR call a configured MCP tool now "
                + "{\"tool\":\"server__tool\",\"arguments\":{...}} "
                + "(e.g. …__ask_zuora, …__query_objects).",
                $"Rejeitado: `{toolName}` precisa de \"{field}\" não vazio. "
                + $"Repete como APENAS JSON {{\"tool\":\"{toolName}\",\"arguments\":{{\"{field}\":\"palavras concretas\"}}}} "
                + "OU chama agora uma tool MCP "
                + "{\"tool\":\"server__tool\",\"arguments\":{...}} "
                + "(ex. …__ask_zuora, …__query_objects).");
        }

        return TenantLocale.Select(
            lang,
            $"Rejected: `{toolName}` needs a non-empty \"{field}\". "
            + $"Retry as ONLY JSON {{\"tool\":\"{toolName}\",\"arguments\":{{\"{field}\":\"concrete keywords\"}}}}.",
            $"Rejeitado: `{toolName}` precisa de \"{field}\" não vazio. "
            + $"Repete como APENAS JSON {{\"tool\":\"{toolName}\",\"arguments\":{{\"{field}\":\"palavras concretas\"}}}}.");
    }

    private static string BuildFeedback(string toolName, AppRuntimeConfig runtimeConfig, bool afterFailure)
    {
        var lang = runtimeConfig.DefaultLanguage;
        var isWiki = QueryFocusedTools.Contains(NormalizeToolName(toolName));

        // Identical call that already succeeded: stop tooling and answer from that result.
        if (!afterFailure)
        {
            if (isWiki)
            {
                return TenantLocale.Select(
                    lang,
                    "Rejected: identical wiki_search already succeeded — do NOT repeat the same query. "
                    + "Answer the user NOW from the wiki result already gathered. "
                    + "No tool_calls, no JSON tool invocations.",
                    "Rejeitado: wiki_search idêntica já teve sucesso — NÃO repitas a mesma query. "
                    + "Responde AGORA ao utilizador com o resultado wiki já obtido. "
                    + "Sem tool_calls, sem invocações JSON de tools.");
            }

            return TenantLocale.Select(
                lang,
                $"Rejected: identical `{toolName}` already succeeded — do NOT repeat the same arguments. "
                + "Answer the user NOW from the tool result already gathered. "
                + "No tool_calls, no JSON tool invocations.",
                $"Rejeitado: `{toolName}` idêntica já teve sucesso — NÃO repitas os mesmos arguments. "
                + "Responde AGORA ao utilizador com o resultado da tool já obtido. "
                + "Sem tool_calls, sem invocações JSON de tools.");
        }

        var hasMcp = HasConfiguredMcp(runtimeConfig);
        var prior = TenantLocale.Select(lang, "already failed", "já falhou");

        if (hasMcp && isWiki)
        {
            return TenantLocale.Select(
                lang,
                $"Rejected: identical wiki_search {prior} — do NOT repeat the same query. "
                + "Either change the query substantially OR call a configured MCP tool now as ONLY JSON "
                + "{\"tool\":\"server__tool\",\"arguments\":{...}} "
                + "(e.g. …__ask_zuora, …__query_objects; tool_describe if the schema is unclear).",
                $"Rejeitado: wiki_search idêntica {prior} — NÃO repitas a mesma query. "
                + "Ou muda a query de forma substancial OU chama agora uma tool MCP configurada como APENAS JSON "
                + "{\"tool\":\"server__tool\",\"arguments\":{...}} "
                + "(ex. …__ask_zuora, …__query_objects; tool_describe se o schema for unclear).");
        }

        if (hasMcp)
        {
            return TenantLocale.Select(
                lang,
                $"Rejected: identical `{toolName}` {prior} — do NOT repeat the same arguments. "
                + "Try different arguments or another MCP/catalog tool as ONLY JSON "
                + "{\"tool\":\"name\",\"arguments\":{...}}.",
                $"Rejeitado: `{toolName}` idêntica {prior} — NÃO repitas os mesmos arguments. "
                + "Tenta arguments diferentes ou outra tool MCP/catálogo como APENAS JSON "
                + "{\"tool\":\"nome\",\"arguments\":{...}}.");
        }

        if (isWiki)
        {
            return TenantLocale.Select(
                lang,
                $"Rejected: identical wiki_search {prior} — do NOT repeat. "
                + "Change the query substantially or answer from the evidence you already have.",
                $"Rejeitado: wiki_search idêntica {prior} — NÃO repitas. "
                + "Muda a query de forma substancial ou responde com a evidência que já tens.");
        }

        return TenantLocale.Select(
            lang,
            $"Rejected: identical `{toolName}` {prior} — do NOT repeat the same arguments. "
            + "Change arguments or answer from existing evidence.",
            $"Rejeitado: `{toolName}` idêntica {prior} — NÃO repitas os mesmos arguments. "
            + "Muda os arguments ou responde com a evidência existente.");
    }

    private static bool HasConfiguredMcp(AppRuntimeConfig runtimeConfig) =>
        runtimeConfig.Agentic.Tools.Integrations.Any(i =>
            string.Equals(i.Type, "mcp", StringComparison.OrdinalIgnoreCase)
            && i.Enabled
            && i.IsConfigured);
}
