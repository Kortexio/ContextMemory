using ContextMemory.Core.Agentic.Prompts;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic.Harness;

/// <summary>
/// Per-model harness knobs (CodeForge-style). Derived from <see cref="LlmCapabilities"/>;
/// Weak locals get aggressive repair/families/compactor; Strong stays native-first.
/// </summary>
public sealed record ModelHarnessProfile(
    ModelHarnessMode Mode,
    int MaxRefunds,
    int StallThreshold,
    int MaxFinishNudges,
    int MaxContinueExtensions,
    int AbsoluteMaxIterationsCeiling,
    bool CompactSchemas,
    bool PreferFullSchemas,
    bool EnableEvidenceFinishGate,
    bool SoftenForceAnswerOnly,
    IReadOnlyList<string> FamilyIds)
{
    public const int HardAbsoluteMaxIterations = 40;

    public static ModelHarnessProfile From(AppRuntimeConfig config)
    {
        var caps = LlmCapabilitiesResolver.From(config);
        var profile = AgenticPromptProfileResolver.Resolve(config);

        if (caps.HarnessMode == ModelHarnessMode.Off)
        {
            return new ModelHarnessProfile(
                Mode: ModelHarnessMode.Off,
                MaxRefunds: 0,
                StallThreshold: int.MaxValue,
                MaxFinishNudges: 0,
                MaxContinueExtensions: 0,
                AbsoluteMaxIterationsCeiling: HardAbsoluteMaxIterations,
                CompactSchemas: false,
                PreferFullSchemas: true,
                EnableEvidenceFinishGate: false,
                SoftenForceAnswerOnly: false,
                FamilyIds: Array.Empty<string>());
        }

        var weak = caps.HarnessMode == ModelHarnessMode.Weak;

        return new ModelHarnessProfile(
            Mode: caps.HarnessMode,
            MaxRefunds: weak ? 12 : 6,
            StallThreshold: weak ? 3 : 4,
            MaxFinishNudges: weak ? 3 : 2,
            MaxContinueExtensions: weak ? 8 : 4,
            AbsoluteMaxIterationsCeiling: HardAbsoluteMaxIterations,
            CompactSchemas: weak,
            PreferFullSchemas: true,
            EnableEvidenceFinishGate: true,
            // Keep false by default so wiki-budget / duplicate-after-success still strip tools
            // (E2E circuit breaker). Enable via future config when pivoting without evidence.
            SoftenForceAnswerOnly: false,
            FamilyIds: ResolveFamilyIds(profile, config.LlmModel));
    }

    public static IReadOnlyList<string> ResolveFamilyIds(AgenticPromptProfile profile, string? model)
    {
        var all = FamilyIdsAll;
        // Priority order by profile / model name; registry still contains every family.
        var preferred = profile switch
        {
            AgenticPromptProfile.Qwen => new[]
            {
                "qwen", "deepseek-r1", "dsml", "glm", "harmony", "mistral", "llama",
                "kimi", "gemma4", "granite", "minimax", "minimax-text", "olmo", "phi4"
            },
            AgenticPromptProfile.Ollama => new[]
            {
                "llama", "qwen", "mistral", "harmony", "deepseek-r1", "granite", "gemma4",
                "phi4", "olmo", "glm", "dsml", "kimi", "minimax", "minimax-text"
            },
            AgenticPromptProfile.Claude or AgenticPromptProfile.OpenAi or AgenticPromptProfile.ComposerLike
                => new[]
                {
                    "harmony", "mistral", "qwen", "llama", "deepseek-r1", "dsml", "gemma4",
                    "kimi", "glm", "granite", "minimax", "minimax-text", "olmo", "phi4"
                },
            _ => all.ToArray()
        };

        if (!string.IsNullOrWhiteSpace(model))
        {
            preferred = BoostByModelName(model, preferred);
        }

        // Ensure every registered id appears (preferred first, then remainder).
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<string>(all.Count);
        foreach (var id in preferred.Concat(all))
        {
            if (seen.Add(id))
                ordered.Add(id);
        }

        return ordered;
    }

    public static readonly IReadOnlyList<string> FamilyIdsAll =
    [
        "qwen", "deepseek-r1", "dsml", "gemma4", "harmony", "mistral", "llama",
        "kimi", "glm", "granite", "minimax", "minimax-text", "olmo", "phi4"
    ];

    private static string[] BoostByModelName(string model, string[] preferred)
    {
        string? boost = null;
        if (Contains(model, "qwen", "hermes", "bonsai")) boost = "qwen";
        else if (Contains(model, "deepseek", "r1")) boost = "deepseek-r1";
        else if (Contains(model, "gemma")) boost = "gemma4";
        else if (Contains(model, "mistral", "mixtral")) boost = "mistral";
        else if (Contains(model, "llama", "meta-llama")) boost = "llama";
        else if (Contains(model, "kimi", "moonshot")) boost = "kimi";
        else if (Contains(model, "glm", "chatglm")) boost = "glm";
        else if (Contains(model, "granite")) boost = "granite";
        else if (Contains(model, "minimax")) boost = "minimax";
        else if (Contains(model, "olmo")) boost = "olmo";
        else if (Contains(model, "phi")) boost = "phi4";
        else if (Contains(model, "gpt-oss", "harmony", "o1", "o3")) boost = "harmony";

        if (boost is null)
            return preferred;

        return new[] { boost }.Concat(preferred.Where(p => !p.Equals(boost, StringComparison.OrdinalIgnoreCase))).ToArray();
    }

    private static bool Contains(string haystack, params string[] needles) =>
        needles.Any(n => haystack.Contains(n, StringComparison.OrdinalIgnoreCase));
}
