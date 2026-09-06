namespace ContextMemory.Core.Agentic;

public sealed record ContextPolicy
{
    public bool AllowConfidentialContent { get; init; } = true;
    public IReadOnlyList<string> DeniedContextPatterns { get; init; } = [];
    public int MaxStaticContextChars { get; init; } = 0; // 0 = unlimited
}

public sealed record CapabilityPolicy
{
    public IReadOnlyList<string> AllowedToolPatterns { get; init; } = ["*"];
    public IReadOnlyList<string> DeniedToolPatterns { get; init; } = [];
}

public sealed record ExecutionPolicy
{
    public IReadOnlyList<string> RequireConfirmationFor { get; init; } = [];
    public string NetworkEgress { get; init; } = "restricted"; // deny|restricted|unrestricted
    public IReadOnlyList<string> AllowedEgressHosts { get; init; } = [];
}

public sealed record ResolvedPolicies
{
    public required ResolvedAgenticPolicy Catalog { get; init; }
    public ContextPolicy Context { get; init; } = new();
    public CapabilityPolicy Capability { get; init; } = new();
    public ExecutionPolicy Execution { get; init; } = new();
}

public enum SecretClassification
{
    Public,
    Internal,
    Confidential,
    Secret,
    Restricted
}

public enum ExecutionPolicyDecision
{
    Allow,
    Deny,
    RequireConfirm
}

public static class PolicyLayersFactory
{
    /// <summary>
    /// Builds layered policies from catalog + existing <see cref="AgenticGuardrailsConfig"/>.
    /// </summary>
    public static ResolvedPolicies FromGuardrails(
        ResolvedAgenticPolicy catalog,
        AgenticGuardrailsConfig guardrails) =>
        new()
        {
            Catalog = catalog,
            Context = new ContextPolicy(),
            Capability = new CapabilityPolicy(),
            Execution = new ExecutionPolicy
            {
                RequireConfirmationFor = guardrails.RequireConfirmationFor,
                NetworkEgress = string.IsNullOrWhiteSpace(guardrails.NetworkEgress)
                    ? "restricted"
                    : guardrails.NetworkEgress,
                AllowedEgressHosts = guardrails.AllowedEgressHosts
            }
        };
}
