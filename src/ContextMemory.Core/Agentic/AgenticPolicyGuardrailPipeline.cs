using ContextMemory.Core.Contracts;
using ContextMemory.Core.Localization;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

/// <summary>
/// Ordered policy-kind pipeline (core safety + extended catalog).
/// Validators depend on this instead of duplicating kind switches (OCP / DIP).
/// Marker lists and feedback come from Admin <c>ConfigJson</c>; engines only apply algorithms.
/// </summary>
public static class AgenticPolicyGuardrailPipeline
{
    /// <summary>
    /// Core kinds that must run before length/blocked-pattern checks and before LLM judge.
    /// </summary>
    public static ValidationResult? TryRejectCore(AgentValidationRequest request)
    {
        var policy = request.RuntimeConfig.ResolvedPolicy;
        var answer = request.FinalAnswer;
        var steps = request.Steps;
        var config = request.RuntimeConfig;

        if (AgenticGuardrailConfigReader.TryGetKind(policy, AgenticGuardrailKinds.ThinkingLeak, out var thinkingJson)
            && AgenticThinkingLeakGuardrail.TryGetRejectionFeedback(
                answer, thinkingJson, config, out var thinkingFeedback))
        {
            return ValidationResult.Reject(thinkingFeedback);
        }

        if (AgenticGuardrailConfigReader.TryGetKind(policy, AgenticGuardrailKinds.SandboxClaim, out var sandboxJson)
            && AgenticSandboxClaimGuardrail.TryGetRejectionFeedback(
                answer, steps, sandboxJson, config, out var sandboxFeedback))
        {
            return ValidationResult.Reject(
                ValidationMessages.FabricatedSandboxLimitation(sandboxFeedback, config));
        }

        if (AgenticGuardrailConfigReader.TryGetKind(policy, AgenticGuardrailKinds.UrlFetch, out var urlJson)
            && AgenticUrlFetchGuardrail.TryGetRejectionFeedback(
                request.UserObjective, answer, steps, urlJson, config, out var urlFeedback))
        {
            return ValidationResult.Reject(
                ValidationMessages.UrlDescribedWithoutFetch(urlFeedback, config));
        }

        if (AgenticGuardrailConfigReader.TryGetKind(policy, AgenticGuardrailKinds.LiveDataEvidence, out var liveJson)
            && AgenticLiveDataEvidenceGuardrail.TryGetRejectionFeedback(
                request.UserObjective, answer, steps, liveJson, config, out var liveFeedback))
        {
            return ValidationResult.Reject(
                ValidationMessages.LiveDataWithoutEvidence(liveFeedback, config));
        }

        if (AgenticGuardrailConfigReader.TryGetKind(policy, AgenticGuardrailKinds.ToolSurfaceHidden, out var toolJson)
            && AgenticToolIntentNarrationGuardrail.TryGetRejectionFeedback(
                answer, steps, toolJson, config, out var toolFeedback))
        {
            return ValidationResult.Reject(
                ValidationMessages.ToolIntentNarration(toolFeedback, config));
        }

        return null;
    }

    public static async Task<string?> TryGetExtendedRejectionAsync(
        AgentValidationRequest request,
        IAgenticUrlAvailabilityChecker? urlChecker,
        CancellationToken cancellationToken = default)
    {
        var policy = request.RuntimeConfig.ResolvedPolicy;
        var answer = request.FinalAnswer;
        var steps = request.Steps;
        var objective = request.UserObjective;
        var config = request.RuntimeConfig;

        foreach (var kind in PatternListKinds)
        {
            if (!AgenticGuardrailConfigReader.TryGetKind(policy, kind, out var json))
                continue;
            if (AgenticGuardrailConfigReader.TryMatchPatterns(answer, json, config.DefaultLanguage, out var fb))
                return fb;
        }

        if (AgenticGuardrailConfigReader.TryGetKind(policy, AgenticGuardrailKinds.PromptInjection, out var injectionJson))
        {
            var blob = $"{objective}\n{answer}";
            if (AgenticGuardrailConfigReader.TryMatchPatterns(blob, injectionJson, config.DefaultLanguage, out var fb))
                return fb;
        }

        if (AgenticGuardrailConfigReader.TryGetKind(policy, AgenticGuardrailKinds.SensitivePii, out var piiJson)
            && AgenticPiiGuardrail.TryGetRejectionFeedback(answer, piiJson, config, out var piiFb))
            return piiFb;

        if (AgenticGuardrailConfigReader.TryGetKind(policy, AgenticGuardrailKinds.Gibberish, out var gibJson)
            && AgenticGibberishGuardrail.TryGetRejectionFeedback(answer, gibJson, config, out var gibFb))
            return gibFb;

        if (AgenticGuardrailConfigReader.TryGetKind(policy, AgenticGuardrailKinds.DuplicateSentence, out var dupJson)
            && AgenticDuplicateSentenceGuardrail.TryGetRejectionFeedback(answer, dupJson, config, out var dupFb))
            return dupFb;

        if (policy.HasKind(AgenticGuardrailKinds.SourceContext)
            || policy.HasKind(AgenticGuardrailKinds.FactCheck))
        {
            var kind = policy.HasKind(AgenticGuardrailKinds.SourceContext)
                ? AgenticGuardrailKinds.SourceContext
                : AgenticGuardrailKinds.FactCheck;
            var json = AgenticGuardrailConfigReader.ForKind(policy, kind);
            if (AgenticSourceGroundingGuardrail.TryGetRejectionFeedback(answer, steps, json, config, out var srcFb))
                return srcFb;
        }

        if (AgenticGuardrailConfigReader.TryGetKind(policy, AgenticGuardrailKinds.NumericGrounding, out var numJson)
            && AgenticNumericsGroundingGuardrail.TryGetRejectionFeedback(answer, steps, numJson, config, out var numFb))
            return numFb;

        if (AgenticGuardrailConfigReader.TryGetKind(policy, AgenticGuardrailKinds.PriceQuote, out var priceJson)
            && AgenticPriceQuoteGuardrail.TryGetRejectionFeedback(answer, steps, priceJson, config, out var priceFb))
            return priceFb;

        if (AgenticGuardrailConfigReader.TryGetKind(policy, AgenticGuardrailKinds.PromptAddress, out var addrJson)
            && AgenticPromptAddressGuardrail.TryGetRejectionFeedback(objective, answer, addrJson, config, out var addrFb))
            return addrFb;

        if (AgenticGuardrailConfigReader.TryGetKind(policy, AgenticGuardrailKinds.Relevance, out var relJson)
            && AgenticRelevanceGuardrail.TryGetRejectionFeedback(objective, answer, relJson, config, out var relFb))
            return relFb;

        if (AgenticGuardrailConfigReader.TryGetKind(policy, AgenticGuardrailKinds.SqlQuery, out var sqlJson)
            && AgenticSqlGuardrail.TryGetRejectionFeedback(answer, sqlJson, config, out var sqlFb))
            return sqlFb;

        if (AgenticGuardrailConfigReader.TryGetKind(policy, AgenticGuardrailKinds.JsonFormat, out var jsonFmt)
            && AgenticJsonSchemaGuardrail.TryGetRejectionFeedback(
                AgenticGuardrailKinds.JsonFormat, answer, jsonFmt, config, out var jsonFb))
            return jsonFb;

        if (AgenticGuardrailConfigReader.TryGetKind(policy, AgenticGuardrailKinds.OpenApiResponse, out var openApiJson)
            && AgenticJsonSchemaGuardrail.TryGetRejectionFeedback(
                AgenticGuardrailKinds.OpenApiResponse, answer, openApiJson, config, out var openFb))
            return openFb;

        if (AgenticGuardrailConfigReader.TryGetKind(policy, AgenticGuardrailKinds.UrlAvailability, out var urlAvailJson))
        {
            var (reject, fb) = await AgenticUrlAvailabilityGuardrail.TryGetRejectionFeedbackAsync(
                    answer, urlAvailJson, config, urlChecker, cancellationToken)
                .ConfigureAwait(false);
            if (reject)
                return fb;
        }

        return null;
    }

    private static readonly string[] PatternListKinds =
    [
        AgenticGuardrailKinds.InappropriateContent,
        AgenticGuardrailKinds.OffensiveLanguage,
        AgenticGuardrailKinds.CompetitorMention
    ];
}
