namespace ContextMemory.Core.Agentic;

/// <summary>How a catalog skill/rule is injected into agent context.</summary>
public static class AgenticSkillActivation
{
    public const string Skill = "skill";
    public const string AlwaysOn = "always_on";
    public const string Requestable = "requestable";

    public static bool IsRule(string? activation) =>
        string.Equals(activation, AlwaysOn, StringComparison.OrdinalIgnoreCase)
        || string.Equals(activation, Requestable, StringComparison.OrdinalIgnoreCase);

    public static bool IsAlwaysOn(string? activation) =>
        string.Equals(activation, AlwaysOn, StringComparison.OrdinalIgnoreCase);

    public static bool IsRequestable(string? activation) =>
        string.Equals(activation, Requestable, StringComparison.OrdinalIgnoreCase);

    public static bool IsSkill(string? activation) =>
        string.IsNullOrWhiteSpace(activation)
        || string.Equals(activation, Skill, StringComparison.OrdinalIgnoreCase);
}
