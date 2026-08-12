using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

/// <summary>
/// Runs the extended LLM-guardrails catalog kinds (image) when present in <see cref="ResolvedAgenticPolicy"/>.
/// Soft kinds (logical-flow, quality, …) are handled by the LLM judge prompt, not here.
/// </summary>
public static class AgenticExtendedGuardrailRunner
{
    public static async Task<string?> TryGetRejectionAsync(
        AgentValidationRequest request,
        IAgenticUrlAvailabilityChecker? urlChecker,
        CancellationToken cancellationToken = default)
    {
        var policy = request.RuntimeConfig.ResolvedPolicy;
        var answer = request.FinalAnswer;
        var steps = request.Steps;
        var objective = request.UserObjective;
        var config = request.RuntimeConfig;

        // Pattern lists
        foreach (var kind in new[]
                 {
                     AgenticGuardrailKinds.InappropriateContent,
                     AgenticGuardrailKinds.OffensiveLanguage,
                     AgenticGuardrailKinds.CompetitorMention
                 })
        {
            if (!policy.HasKind(kind))
                continue;
            var json = policy.FindByKind(kind)?.ConfigJson ?? "{}";
            if (AgenticPatternListGuardrail.TryGetRejectionFeedback(kind, answer, json, config, out var fb))
                return fb;
        }

        if (policy.HasKind(AgenticGuardrailKinds.PromptInjection))
        {
            var json = policy.FindByKind(AgenticGuardrailKinds.PromptInjection)?.ConfigJson ?? "{}";
            if (AgenticPromptInjectionGuardrail.TryGetRejectionFeedback(objective, answer, json, config, out var fb))
                return fb;
        }

        if (policy.HasKind(AgenticGuardrailKinds.SensitivePii))
        {
            var json = policy.FindByKind(AgenticGuardrailKinds.SensitivePii)?.ConfigJson ?? "{}";
            if (AgenticPiiGuardrail.TryGetRejectionFeedback(answer, json, config, out var fb))
                return fb;
        }

        if (policy.HasKind(AgenticGuardrailKinds.Gibberish))
        {
            var json = policy.FindByKind(AgenticGuardrailKinds.Gibberish)?.ConfigJson ?? "{}";
            if (AgenticGibberishGuardrail.TryGetRejectionFeedback(answer, json, config, out var fb))
                return fb;
        }

        if (policy.HasKind(AgenticGuardrailKinds.DuplicateSentence))
        {
            var json = policy.FindByKind(AgenticGuardrailKinds.DuplicateSentence)?.ConfigJson ?? "{}";
            if (AgenticDuplicateSentenceGuardrail.TryGetRejectionFeedback(answer, json, config, out var fb))
                return fb;
        }

        if (policy.HasKind(AgenticGuardrailKinds.SourceContext)
            || policy.HasKind(AgenticGuardrailKinds.FactCheck))
        {
            var kind = policy.HasKind(AgenticGuardrailKinds.SourceContext)
                ? AgenticGuardrailKinds.SourceContext
                : AgenticGuardrailKinds.FactCheck;
            var json = policy.FindByKind(kind)?.ConfigJson ?? "{}";
            if (AgenticSourceGroundingGuardrail.TryGetRejectionFeedback(answer, steps, json, config, out var fb))
                return fb;
        }

        if (policy.HasKind(AgenticGuardrailKinds.NumericGrounding))
        {
            var json = policy.FindByKind(AgenticGuardrailKinds.NumericGrounding)?.ConfigJson ?? "{}";
            if (AgenticNumericsGroundingGuardrail.TryGetRejectionFeedback(answer, steps, json, config, out var fb))
                return fb;
        }

        if (policy.HasKind(AgenticGuardrailKinds.PriceQuote))
        {
            var json = policy.FindByKind(AgenticGuardrailKinds.PriceQuote)?.ConfigJson ?? "{}";
            if (AgenticPriceQuoteGuardrail.TryGetRejectionFeedback(answer, steps, json, config, out var fb))
                return fb;
        }

        if (policy.HasKind(AgenticGuardrailKinds.PromptAddress))
        {
            var json = policy.FindByKind(AgenticGuardrailKinds.PromptAddress)?.ConfigJson ?? "{}";
            if (AgenticPromptAddressGuardrail.TryGetRejectionFeedback(objective, answer, json, config, out var fb))
                return fb;
        }

        if (policy.HasKind(AgenticGuardrailKinds.Relevance))
        {
            var json = policy.FindByKind(AgenticGuardrailKinds.Relevance)?.ConfigJson ?? "{}";
            if (AgenticRelevanceGuardrail.TryGetRejectionFeedback(objective, answer, json, config, out var fb))
                return fb;
        }

        if (policy.HasKind(AgenticGuardrailKinds.SqlQuery))
        {
            var json = policy.FindByKind(AgenticGuardrailKinds.SqlQuery)?.ConfigJson ?? "{}";
            if (AgenticSqlGuardrail.TryGetRejectionFeedback(answer, json, config, out var fb))
                return fb;
        }

        if (policy.HasKind(AgenticGuardrailKinds.JsonFormat))
        {
            var json = policy.FindByKind(AgenticGuardrailKinds.JsonFormat)?.ConfigJson ?? "{}";
            if (AgenticJsonSchemaGuardrail.TryGetRejectionFeedback(
                    AgenticGuardrailKinds.JsonFormat, answer, json, config, out var fb))
                return fb;
        }

        if (policy.HasKind(AgenticGuardrailKinds.OpenApiResponse))
        {
            var json = policy.FindByKind(AgenticGuardrailKinds.OpenApiResponse)?.ConfigJson ?? "{}";
            if (AgenticJsonSchemaGuardrail.TryGetRejectionFeedback(
                    AgenticGuardrailKinds.OpenApiResponse, answer, json, config, out var fb))
                return fb;
        }

        if (policy.HasKind(AgenticGuardrailKinds.UrlAvailability))
        {
            var json = policy.FindByKind(AgenticGuardrailKinds.UrlAvailability)?.ConfigJson ?? "{}";
            var (reject, fb) = await AgenticUrlAvailabilityGuardrail.TryGetRejectionFeedbackAsync(
                    answer, json, config, urlChecker, cancellationToken)
                .ConfigureAwait(false);
            if (reject)
                return fb;
        }

        return null;
    }
}
