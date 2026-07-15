using ContextMemory.Core.Agentic.Prompts;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Microsoft.Extensions.Logging;

namespace ContextMemory.Core.Agentic;

public sealed class LlmJudgeAgentValidator
{
    private readonly ILlmAdapterResolver _adapterResolver;
    private readonly ILogger<LlmJudgeAgentValidator> _logger;

    public LlmJudgeAgentValidator(
        ILlmAdapterResolver adapterResolver,
        ILogger<LlmJudgeAgentValidator> logger)
    {
        _adapterResolver = adapterResolver;
        _logger = logger;
    }

    public async Task<ValidationResult> ValidateAsync(
        AgentValidationRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var adapter = _adapterResolver.Resolve(request.RuntimeConfig.LlmBackend);
            var prompt = AgenticJudgePromptBuilder.Build(request);

            var response = await adapter.GenerateAsync(
                    new OllamaGenerateRequest
                    {
                        Model = request.RuntimeConfig.LlmModel,
                        Prompt = prompt,
                        Stream = false,
                        Format = "json"
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            var raw = OllamaLlmText.NormalizeAssistantContent(
                response.Response ?? OllamaLlmText.GetMessageContent(response.Message));

            return AgentJudgeResponseParser.Parse(raw, request.RuntimeConfig.DefaultLanguage);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM-as-judge failed for app {AppId}, accepting answer", request.RuntimeConfig.AppId);
            return ValidationResult.Ok();
        }
    }
}
