using System.Text.Json.Serialization;

namespace ContextMemory.Core.Models;

/// <summary>
/// Per-app defaults for LLM generation / sampling fields sent on chat and generate requests.
/// Null or empty fields mean “leave server/model default”. Request body values override these.
/// </summary>
public record LlmGenerationConfig
{
    [JsonPropertyName("temperature")]
    public float? Temperature { get; init; }

    [JsonPropertyName("topP")]
    public float? TopP { get; init; }

    [JsonPropertyName("topK")]
    public int? TopK { get; init; }

    /// <summary>Context window size (Ollama <c>num_ctx</c>). Critical for tool-heavy agentic turns.</summary>
    [JsonPropertyName("numCtx")]
    public int? NumCtx { get; init; }

    [JsonPropertyName("repeatPenalty")]
    public float? RepeatPenalty { get; init; }

    [JsonPropertyName("seed")]
    public int? Seed { get; init; }

    /// <summary>Stop sequences (exact strings). Empty list = none.</summary>
    [JsonPropertyName("stop")]
    public List<string>? Stop { get; init; }

    /// <summary>Max tokens to generate (Ollama <c>num_predict</c> / OpenAI <c>max_tokens</c>).</summary>
    [JsonPropertyName("numPredict")]
    public int? NumPredict { get; init; }

    [JsonPropertyName("tfsZ")]
    public float? TfsZ { get; init; }

    [JsonPropertyName("mirostat")]
    public int? Mirostat { get; init; }

    /// <summary>Ollama <c>keep_alive</c> (e.g. <c>5m</c>, <c>-1</c>). Empty = server default.</summary>
    [JsonPropertyName("keepAlive")]
    public string? KeepAlive { get; init; }

    /// <summary>Ollama <c>format</c> (e.g. <c>json</c>). Empty = free text.</summary>
    [JsonPropertyName("format")]
    public string? Format { get; init; }

    [JsonIgnore]
    public bool HasAnyValue =>
        Temperature is not null
        || TopP is not null
        || TopK is not null
        || NumCtx is not null
        || RepeatPenalty is not null
        || Seed is not null
        || (Stop is { Count: > 0 })
        || NumPredict is not null
        || TfsZ is not null
        || Mirostat is not null
        || !string.IsNullOrWhiteSpace(KeepAlive)
        || !string.IsNullOrWhiteSpace(Format);

    public OllamaOptions ToOllamaOptions() => new()
    {
        Temperature = Temperature,
        TopP = TopP,
        TopK = TopK,
        NumCtx = NumCtx,
        RepeatPenalty = RepeatPenalty,
        Seed = Seed,
        Stop = Stop is { Count: > 0 } ? Stop : null,
        NumPredict = NumPredict,
        TfsZ = TfsZ,
        Mirostat = Mirostat
    };

    /// <summary>
    /// Tenant defaults fill gaps; explicit request options win.
    /// </summary>
    public static OllamaOptions? MergeOptions(LlmGenerationConfig? tenant, OllamaOptions? request)
    {
        if (tenant is null || !tenant.HasAnyValue)
            return request;
        if (request is null)
            return tenant.ToOllamaOptions();

        return new OllamaOptions
        {
            Temperature = request.Temperature ?? tenant.Temperature,
            TopP = request.TopP ?? tenant.TopP,
            TopK = request.TopK ?? tenant.TopK,
            NumCtx = request.NumCtx ?? tenant.NumCtx,
            RepeatPenalty = request.RepeatPenalty ?? tenant.RepeatPenalty,
            Seed = request.Seed ?? tenant.Seed,
            Stop = request.Stop is { Count: > 0 } ? request.Stop : tenant.Stop,
            NumPredict = request.NumPredict ?? tenant.NumPredict,
            TfsZ = request.TfsZ ?? tenant.TfsZ,
            Mirostat = request.Mirostat ?? tenant.Mirostat
        };
    }

    public static string? MergeKeepAlive(LlmGenerationConfig? tenant, string? request) =>
        !string.IsNullOrWhiteSpace(request) ? request : tenant?.KeepAlive;

    public static string? MergeFormat(LlmGenerationConfig? tenant, string? request) =>
        !string.IsNullOrWhiteSpace(request) ? request : tenant?.Format;
}
