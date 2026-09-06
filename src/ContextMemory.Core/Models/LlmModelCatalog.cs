using ContextMemory.Core.Contracts;

namespace ContextMemory.Core.Models;

/// <summary>Capability flags for a catalogued LLM.</summary>
public sealed record LlmModelCapabilities(
    bool Tools = false,
    bool Vision = false,
    bool Reasoning = false);

/// <summary>Static descriptor for a known or generic LLM target.</summary>
public sealed record LlmModelDescriptor(
    string Id,
    string DisplayName,
    LlmModelCapabilities Capabilities,
    decimal CostPer1kInput = 0m,
    decimal CostPer1kOutput = 0m,
    int ContextWindow = 8_192,
    string[]? PreferredTasks = null);

/// <summary>Lookup of seeded / known LLM descriptors for routing.</summary>
public interface ILlmModelRegistry
{
    IReadOnlyList<LlmModelDescriptor> All { get; }

    LlmModelDescriptor? FindById(string? modelId);

    IReadOnlyList<LlmModelDescriptor> FindByPreferredTask(LlmTaskType task);

    IReadOnlyList<LlmModelDescriptor> FindByCapability(
        bool? tools = null,
        bool? vision = null,
        bool? reasoning = null);
}

/// <summary>In-memory registry with generic OpenAI / Ollama / local defaults.</summary>
public sealed class LlmModelRegistry : ILlmModelRegistry
{
    private readonly IReadOnlyList<LlmModelDescriptor> _models;
    private readonly Dictionary<string, LlmModelDescriptor> _byId;

    public LlmModelRegistry()
        : this(CreateDefaults())
    {
    }

    public LlmModelRegistry(IEnumerable<LlmModelDescriptor> models)
    {
        _models = models.ToList();
        _byId = new Dictionary<string, LlmModelDescriptor>(StringComparer.OrdinalIgnoreCase);
        foreach (var model in _models)
        {
            if (string.IsNullOrWhiteSpace(model.Id))
                continue;
            _byId[model.Id.Trim()] = model;
        }
    }

    public IReadOnlyList<LlmModelDescriptor> All => _models;

    public LlmModelDescriptor? FindById(string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
            return null;
        return _byId.TryGetValue(modelId.Trim(), out var descriptor) ? descriptor : null;
    }

    public IReadOnlyList<LlmModelDescriptor> FindByPreferredTask(LlmTaskType task)
    {
        var name = task.ToString();
        return _models
            .Where(m => m.PreferredTasks is { Length: > 0 }
                        && m.PreferredTasks.Any(t =>
                            string.Equals(t, name, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    public IReadOnlyList<LlmModelDescriptor> FindByCapability(
        bool? tools = null,
        bool? vision = null,
        bool? reasoning = null)
    {
        return _models
            .Where(m =>
                (tools is null || m.Capabilities.Tools == tools)
                && (vision is null || m.Capabilities.Vision == vision)
                && (reasoning is null || m.Capabilities.Reasoning == reasoning))
            .ToList();
    }

    private static IEnumerable<LlmModelDescriptor> CreateDefaults()
    {
        yield return new LlmModelDescriptor(
            Id: "qwen3.5:9b",
            DisplayName: "Qwen 3.5 9B (Ollama)",
            Capabilities: new LlmModelCapabilities(Tools: true, Vision: false, Reasoning: true),
            CostPer1kInput: 0m,
            CostPer1kOutput: 0m,
            ContextWindow: 32_768,
            PreferredTasks: [nameof(LlmTaskType.Chat), nameof(LlmTaskType.ToolSelection), nameof(LlmTaskType.Synthesis)]);

        yield return new LlmModelDescriptor(
            Id: "qwen2.5-vl",
            DisplayName: "Qwen2.5 VL (Ollama)",
            Capabilities: new LlmModelCapabilities(Tools: true, Vision: true, Reasoning: false),
            ContextWindow: 32_768,
            PreferredTasks: [nameof(LlmTaskType.Vision)]);

        yield return new LlmModelDescriptor(
            Id: "llava",
            DisplayName: "LLaVA (Ollama)",
            Capabilities: new LlmModelCapabilities(Tools: false, Vision: true, Reasoning: false),
            ContextWindow: 4_096,
            PreferredTasks: [nameof(LlmTaskType.Vision)]);

        yield return new LlmModelDescriptor(
            Id: "llama3.2",
            DisplayName: "Llama 3.2 (Ollama)",
            Capabilities: new LlmModelCapabilities(Tools: true, Vision: false, Reasoning: false),
            ContextWindow: 128_000,
            PreferredTasks: [nameof(LlmTaskType.Chat), nameof(LlmTaskType.Compaction), nameof(LlmTaskType.Wiki)]);

        yield return new LlmModelDescriptor(
            Id: "gpt-4o",
            DisplayName: "GPT-4o (OpenAI)",
            Capabilities: new LlmModelCapabilities(Tools: true, Vision: true, Reasoning: false),
            CostPer1kInput: 0.0025m,
            CostPer1kOutput: 0.01m,
            ContextWindow: 128_000,
            PreferredTasks:
            [
                nameof(LlmTaskType.Chat),
                nameof(LlmTaskType.Planning),
                nameof(LlmTaskType.Synthesis),
                nameof(LlmTaskType.Vision)
            ]);

        yield return new LlmModelDescriptor(
            Id: "gpt-4o-mini",
            DisplayName: "GPT-4o mini (OpenAI)",
            Capabilities: new LlmModelCapabilities(Tools: true, Vision: true, Reasoning: false),
            CostPer1kInput: 0.00015m,
            CostPer1kOutput: 0.0006m,
            ContextWindow: 128_000,
            PreferredTasks:
            [
                nameof(LlmTaskType.Compaction),
                nameof(LlmTaskType.Wiki),
                nameof(LlmTaskType.ToolSelection)
            ]);

        yield return new LlmModelDescriptor(
            Id: "o3-mini",
            DisplayName: "o3-mini (OpenAI)",
            Capabilities: new LlmModelCapabilities(Tools: true, Vision: false, Reasoning: true),
            CostPer1kInput: 0.0011m,
            CostPer1kOutput: 0.0044m,
            ContextWindow: 200_000,
            PreferredTasks: [nameof(LlmTaskType.Planning), nameof(LlmTaskType.Synthesis)]);

        yield return new LlmModelDescriptor(
            Id: "gpt-4.1",
            DisplayName: "GPT-4.1 (OpenAI)",
            Capabilities: new LlmModelCapabilities(Tools: true, Vision: true, Reasoning: false),
            CostPer1kInput: 0.002m,
            CostPer1kOutput: 0.008m,
            ContextWindow: 1_047_576,
            PreferredTasks: [nameof(LlmTaskType.Planning), nameof(LlmTaskType.Chat)]);

        yield return new LlmModelDescriptor(
            Id: "mistral",
            DisplayName: "Mistral (Ollama)",
            Capabilities: new LlmModelCapabilities(Tools: true, Vision: false, Reasoning: false),
            ContextWindow: 32_768,
            PreferredTasks: [nameof(LlmTaskType.Chat), nameof(LlmTaskType.Compaction)]);

        yield return new LlmModelDescriptor(
            Id: "phi3",
            DisplayName: "Phi-3 (Ollama)",
            Capabilities: new LlmModelCapabilities(Tools: false, Vision: false, Reasoning: false),
            ContextWindow: 4_096,
            PreferredTasks: [nameof(LlmTaskType.Compaction), nameof(LlmTaskType.Wiki)]);
    }
}
