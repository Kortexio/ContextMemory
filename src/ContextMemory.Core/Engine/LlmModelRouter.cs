using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using ContextMemory.Core.Session;
using Microsoft.Extensions.Options;

namespace ContextMemory.Core.Engine;

/// <summary>
/// Task-aware LLM routing with tenant overrides, capability checks, and fallback chain (CM-7).
/// </summary>
public sealed class LlmModelRouter : ILlmModelRouter
{
    private readonly ILlmModelRegistry _registry;
    private readonly ContextMemoryOptions _options;

    public LlmModelRouter(ILlmModelRegistry registry, IOptions<ContextMemoryOptions> options)
    {
        _registry = registry;
        _options = options.Value;
    }

    public ResolvedLlmTarget Route(ModelRoutingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Config);

        var chain = FallbackChain(request);
        var primary = chain[0];
        var fallback = chain.Count > 1 ? chain[1] : null;
        var backend = string.IsNullOrWhiteSpace(request.Config.LlmBackend)
            ? null
            : request.Config.LlmBackend.Trim();

        return new ResolvedLlmTarget(primary, backend, fallback);
    }

    public IReadOnlyList<string> FallbackChain(ModelRoutingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Config);

        var primary = ResolvePrimary(request);
        var wiki = SessionWikiSettings.ResolveWikiLlmModel(request.Config, _options.DefaultWikiLlmModel);
        var platformDefault = string.IsNullOrWhiteSpace(_options.DefaultLlmModel)
            ? request.Config.LlmModel
            : _options.DefaultLlmModel.Trim();

        var chain = new List<string>(3);
        AddDistinct(chain, primary);
        AddDistinct(chain, wiki);
        AddDistinct(chain, platformDefault);

        if (chain.Count == 0)
            chain.Add("qwen3.5:9b");

        return chain;
    }

    private string ResolvePrimary(ModelRoutingRequest request)
    {
        var config = request.Config;

        if (request.RequiresVision || request.Task == LlmTaskType.Vision)
        {
            var vision = PreferVisionModel(config);
            if (!string.IsNullOrWhiteSpace(vision))
                return vision;
        }

        return request.Task switch
        {
            LlmTaskType.Compaction or LlmTaskType.Wiki =>
                SessionWikiSettings.ResolveWikiLlmModel(config, _options.DefaultWikiLlmModel),

            LlmTaskType.Planning =>
                PreferPlanningModel(config) ?? PreferChatModel(config),

            LlmTaskType.ToolSelection or LlmTaskType.Synthesis or LlmTaskType.Chat =>
                PreferChatModel(config),

            LlmTaskType.Vision =>
                PreferVisionModel(config) ?? PreferChatModel(config),

            _ => PreferChatModel(config)
        };
    }

    private static string PreferChatModel(AppRuntimeConfig config) =>
        string.IsNullOrWhiteSpace(config.LlmModel) ? "qwen3.5:9b" : config.LlmModel.Trim();

    /// <summary>
    /// Optional stronger model for Planning when the catalog lists a reasoning-capable
    /// preferred planning target (tenant metadata via registry PreferredTasks).
    /// </summary>
    private string? PreferPlanningModel(AppRuntimeConfig config)
    {
        var chat = PreferChatModel(config);
        var chatDesc = _registry.FindById(chat);

        // Only upgrade when catalog metadata suggests a stronger planning model
        // and the tenant chat model is not already preferred for Planning.
        var chatAlreadyPlanning = chatDesc?.PreferredTasks?.Any(t =>
            string.Equals(t, nameof(LlmTaskType.Planning), StringComparison.OrdinalIgnoreCase)) == true;
        if (chatAlreadyPlanning && chatDesc?.Capabilities.Reasoning == true)
            return chat;

        var candidates = _registry.FindByPreferredTask(LlmTaskType.Planning)
            .Where(m => m.Capabilities.Reasoning)
            .OrderByDescending(m => m.ContextWindow)
            .ThenByDescending(m => m.CostPer1kInput)
            .ToList();

        if (candidates.Count == 0)
            return null;

        // Prefer a catalog hit that matches the configured chat id family, else first reasoning planner.
        var sameFamily = candidates.FirstOrDefault(c =>
            chat.Contains(c.Id, StringComparison.OrdinalIgnoreCase)
            || c.Id.Contains(chat, StringComparison.OrdinalIgnoreCase));
        if (sameFamily is not null)
            return sameFamily.Id;

        // Only auto-upgrade when think/reasoning is enabled (tenant "metadata" signal).
        if (!config.LlmThinkEnabled && !IsStrongHarness(config))
            return null;

        return candidates[0].Id;
    }

    private string? PreferVisionModel(AppRuntimeConfig config)
    {
        var chat = PreferChatModel(config);
        if (_registry.FindById(chat)?.Capabilities.Vision == true)
            return chat;

        if (LlmCapabilitiesLooksLikeVision(chat))
            return chat;

        var preferred = _registry.FindByPreferredTask(LlmTaskType.Vision)
            .FirstOrDefault(m => m.Capabilities.Vision);
        return preferred?.Id;
    }

    private static bool IsStrongHarness(AppRuntimeConfig config) =>
        string.Equals(config.Agentic.HarnessMode?.Trim(), "strong", StringComparison.OrdinalIgnoreCase);

    private static bool LlmCapabilitiesLooksLikeVision(string model) =>
        Agentic.Prompts.LlmCapabilitiesResolver.LooksLikeVisionModel(model);

    private static void AddDistinct(List<string> chain, string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
            return;
        var trimmed = model.Trim();
        if (chain.Any(m => string.Equals(m, trimmed, StringComparison.OrdinalIgnoreCase)))
            return;
        chain.Add(trimmed);
    }
}
