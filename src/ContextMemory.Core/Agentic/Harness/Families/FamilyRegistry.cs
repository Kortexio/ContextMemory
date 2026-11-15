namespace ContextMemory.Core.Agentic.Harness.Families;

/// <summary>Catalog of every known family grammar parser.</summary>
public static class FamilyRegistry
{
    public static IReadOnlyList<IGrammarParser> All { get; } =
    [
        new QwenFamilyParser(),
        new DeepSeekR1FamilyParser(),
        new DsmlFamilyParser(),
        new Gemma4FamilyParser(),
        new HarmonyFamilyParser(),
        new MistralFamilyParser(),
        new LlamaFamilyParser(),
        new KimiFamilyParser(),
        new GlmFamilyParser(),
        new GraniteFamilyParser(),
        new MinimaxFamilyParser(),
        new MinimaxTextFamilyParser(),
        new OlmoFamilyParser(),
        new Phi4FamilyParser(),
    ];

    private static readonly Dictionary<string, IGrammarParser> ById =
        All.ToDictionary(p => p.Id, StringComparer.Ordinal);

    public static IGrammarParser? Get(string id) =>
        ById.TryGetValue(id, out var parser) ? parser : null;
}
