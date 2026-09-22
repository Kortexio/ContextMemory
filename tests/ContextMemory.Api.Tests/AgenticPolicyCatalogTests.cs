using ContextMemory.Core.Agentic;
using ContextMemory.Core.Agentic.Prompts;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ContextMemory.Api.Tests;

public sealed class AgenticPolicyPackResolverTests
{
    [Fact]
    public void Seed_ContainsExpectedDefaults()
    {
        Assert.Equal(15, AgenticCatalogSeed.Skills.Count);
        Assert.Equal(29, AgenticCatalogSeed.Guardrails.Count);

        var defaultOnSkills = AgenticCatalogSeed.Skills.Where(s => s.IsDefaultEnabled).Select(s => s.Id).ToHashSet();
        Assert.Equal(
            new HashSet<string>
            {
                "tool-calling-discipline",
                "prefer-mcp-over-adhoc",
                "sandbox-facts-selfhosted",
                "privacy-and-secrets"
            },
            defaultOnSkills);

        Assert.Contains(AgenticCatalogSeed.Skills, s =>
            s.Id == "small-model-abstention"
            && !s.IsDefaultEnabled
            && s.LinkedGuardrailIds.Contains("numeric-grounding"));
        Assert.Contains(AgenticCatalogSeed.Skills, s =>
            s.Id == "wiki-first-for-docs"
            && !s.IsDefaultEnabled
            && s.Activation == AgenticSkillActivation.Skill);
        Assert.Contains(AgenticCatalogSeed.Skills, s =>
            s.Id == "rule-always-evidence"
            && !s.IsDefaultEnabled
            && s.Activation == AgenticSkillActivation.Skill);
        Assert.DoesNotContain(AgenticCatalogSeed.Skills, s => s.Id == "ops-triage-evidence-first");
        Assert.DoesNotContain(AgenticCatalogSeed.Skills, s => s.Id == "zuora-graphql-discover-first");

        var defaultOnGuards = AgenticCatalogSeed.Guardrails.Where(g => g.IsDefaultEnabled).Select(g => g.Id).ToHashSet();
        Assert.Equal(
            new HashSet<string>
            {
                "tool-surface-hidden",
                "thinking-leak",
                "prompt-injection",
                "sensitive-pii",
                "inappropriate-content",
                "offensive-language",
                "block-credential-leak",
                "pre-tool-deny-rm-rf",
                "post-tool-redact-secrets",
                "sandbox-claim-reject"
            },
            defaultOnGuards);

        Assert.Contains(AgenticCatalogSeed.Guardrails, g =>
            g.Id == "url-fetch-required" && !g.IsDefaultEnabled);
        Assert.Contains(AgenticCatalogSeed.Guardrails, g =>
            g.Id == "fact-check" && !g.IsDefaultEnabled);
        Assert.Contains(AgenticCatalogSeed.Guardrails, g =>
            g.Id == "source-context-verifier" && !g.IsDefaultEnabled);
        Assert.Contains(AgenticCatalogSeed.Guardrails, g =>
            g.Id == "numeric-grounding"
            && g.Kind == AgenticGuardrailKinds.NumericGrounding
            && !g.IsDefaultEnabled);
        Assert.DoesNotContain(AgenticCatalogSeed.Guardrails, g =>
            g.Id == "live-data-evidence-required");
        Assert.Contains(AgenticCatalogSeed.Guardrails, g =>
            g.Id == "thinking-leak" && g.Kind == AgenticGuardrailKinds.ThinkingLeak);
        Assert.Contains(AgenticCatalogSeed.Guardrails, g =>
            g.Id == "tool-surface-hidden" && g.Kind == AgenticGuardrailKinds.ToolSurfaceHidden);
        Assert.Contains(AgenticCatalogSeed.Guardrails, g =>
            g.Id == "prompt-injection" && g.Kind == AgenticGuardrailKinds.PromptInjection);
        Assert.Contains(AgenticCatalogSeed.Guardrails, g => g.Kind == AgenticGuardrailKinds.PostToolUse);
    }

    [Fact]
    public async Task Resolver_AppliesPlatformDefaultEnabled()
    {
        var resolver = new AgenticPolicyPackResolver(
            new InMemoryAgenticPolicyCatalogStore(),
            new InMemoryAgenticAppPolicyCatalogStore(),
            NullLogger<AgenticPolicyPackResolver>.Instance);

        var resolved = await resolver.ResolveAsync(new AppRuntimeConfig
        {
            AppId = "test",
            Agentic = new AgenticConfig
            {
                Enabled = true,
                Tools = new AgenticToolsConfig
                {
                    Execution =
                    [
                        new ExecutionToolConfig
                        {
                            Type = "self-hosted-sandbox",
                            SandboxEndpoint = "http://sandbox:8080"
                        }
                    ]
                }
            }
        });

        Assert.Contains(resolved.ResolvedPolicy.ActiveSkills, s => s.Id == "tool-calling-discipline");
        Assert.Contains(resolved.ResolvedPolicy.ActiveSkills, s => s.Id == "sandbox-facts-selfhosted");
        Assert.Contains(resolved.ResolvedPolicy.ActiveSkills, s => s.Id == "prefer-mcp-over-adhoc");
        Assert.Contains(resolved.ResolvedPolicy.ActiveSkills, s => s.Id == "privacy-and-secrets");
        Assert.DoesNotContain(resolved.ResolvedPolicy.ActiveSkills, s => s.Id == "strict-no-speculation");
        Assert.DoesNotContain(resolved.ResolvedPolicy.ActiveSkills, s => s.Id == "wiki-first-for-docs");
        Assert.DoesNotContain(resolved.ResolvedPolicy.ActiveSkills, s => s.Id == "anti-hallucination-web");
        Assert.DoesNotContain(resolved.ResolvedPolicy.ActiveSkills, s => s.Id == "zuora-graphql-discover-first");
        Assert.DoesNotContain(resolved.ResolvedPolicy.ActiveSkills, s => s.Id == "ops-triage-evidence-first");
        Assert.False(resolved.ResolvedPolicy.HasKind(AgenticGuardrailKinds.UrlFetch));
        Assert.False(resolved.ResolvedPolicy.HasKind(AgenticGuardrailKinds.FactCheck));
        Assert.True(resolved.ResolvedPolicy.HasKind(AgenticGuardrailKinds.SandboxClaim));
        Assert.True(resolved.ResolvedPolicy.HasKind(AgenticGuardrailKinds.ToolSurfaceHidden));
        Assert.True(resolved.ResolvedPolicy.HasKind(AgenticGuardrailKinds.PromptInjection));
        Assert.True(resolved.ResolvedPolicy.HasKind(AgenticGuardrailKinds.ThinkingLeak));
    }

    [Fact]
    public async Task Resolver_SkipsSandboxSkill_WhenNotSelfHosted()
    {
        var resolver = new AgenticPolicyPackResolver(
            new InMemoryAgenticPolicyCatalogStore(),
            new InMemoryAgenticAppPolicyCatalogStore(),
            NullLogger<AgenticPolicyPackResolver>.Instance);

        var resolved = await resolver.ResolveAsync(new AppRuntimeConfig
        {
            AppId = "test",
            Agentic = new AgenticConfig { Enabled = true }
        });

        Assert.DoesNotContain(resolved.ResolvedPolicy.ActiveSkills, s => s.Id == "sandbox-facts-selfhosted");
    }

    [Fact]
    public async Task Resolver_IgnoresLegacyPolicyPacks_AndUnionsAppSkills()
    {
        var appStore = new InMemoryAgenticAppPolicyCatalogStore();
        await appStore.UpsertSkillAsync(new AgenticAppSkillDefinition
        {
            AppId = "test",
            Id = "tenant-only-skill",
            Name = "Tenant only",
            PromptMarkdown = "## Tenant",
            IsEnabled = true,
            SortOrder = 10
        });

        var resolver = new AgenticPolicyPackResolver(
            new InMemoryAgenticPolicyCatalogStore(),
            appStore,
            NullLogger<AgenticPolicyPackResolver>.Instance);

        var resolved = await resolver.ResolveAsync(new AppRuntimeConfig
        {
            AppId = "test",
            Agentic = new AgenticConfig
            {
                PolicyPacks = new PolicyPacksConfig
                {
                    EnabledSkillIds = [],
                    EnabledGuardrailIds = []
                }
            }
        });

        Assert.Contains(resolved.ResolvedPolicy.ActiveSkills, s => s.Id == "tool-calling-discipline");
        Assert.Contains(resolved.ResolvedPolicy.ActiveSkills, s => s.Id == "tenant-only-skill");
        Assert.False(resolved.ResolvedPolicy.HasKind(AgenticGuardrailKinds.UrlFetch));
        Assert.True(resolved.ResolvedPolicy.HasKind(AgenticGuardrailKinds.ToolSurfaceHidden));
        Assert.True(resolved.ResolvedPolicy.HasKind(AgenticGuardrailKinds.PromptInjection));
    }

    [Fact]
    public async Task Resolver_DoesNotLeakAppSkillsAcrossTenants()
    {
        var appStore = new InMemoryAgenticAppPolicyCatalogStore();
        await appStore.UpsertSkillAsync(new AgenticAppSkillDefinition
        {
            AppId = "kyc",
            Id = "kyc-only",
            Name = "KYC",
            PromptMarkdown = "kyc",
            IsEnabled = true
        });

        var resolver = new AgenticPolicyPackResolver(
            new InMemoryAgenticPolicyCatalogStore(),
            appStore,
            NullLogger<AgenticPolicyPackResolver>.Instance);

        var other = await resolver.ResolveAsync(new AppRuntimeConfig { AppId = "other" });
        Assert.DoesNotContain(other.ResolvedPolicy.ActiveSkills, s => s.Id == "kyc-only");

        var kyc = await resolver.ResolveAsync(new AppRuntimeConfig { AppId = "kyc" });
        Assert.Contains(kyc.ResolvedPolicy.ActiveSkills, s => s.Id == "kyc-only");
    }
}

public sealed class AgenticSystemPromptFromSkillsTests
{
    [Fact]
    public void Build_IncludesActiveSkillMarkdown_NotHardcodedAntiHallucination()
    {
        var config = new AppRuntimeConfig
        {
            AppId = "test",
            LlmBackend = "openai",
            LlmModel = "gpt-4o",
            ResolvedPolicy = new ResolvedAgenticPolicy
            {
                ActiveSkills =
                [
                    new AgenticSkillDefinition
                    {
                        Id = "anti-hallucination-web",
                        Name = "Anti-hallucination (web)",
                        IsDefaultEnabled = true,
                        PromptMarkdown = "## Anti-hallucination (web)\n- Fetch URLs first."
                    }
                ]
            }
        };

        var prompt = AgenticSystemPromptBuilder.Build(config, "python_execute");

        Assert.Contains("## Default skills", prompt, StringComparison.Ordinal);
        Assert.Contains("`anti-hallucination-web`", prompt, StringComparison.Ordinal);
        Assert.Contains("skill_read", prompt, StringComparison.OrdinalIgnoreCase);
        // Strong / lazy discovery: full PromptMarkdown is loaded via skill_read, not stuffed here.
        Assert.DoesNotContain("Fetch URLs first", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Anti-alucinação (obrigatório)", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_SkeletonOnly_WhenNoSkills()
    {
        var prompt = AgenticSystemPromptBuilder.Build(
            new AppRuntimeConfig { AppId = "test" },
            "shell_execute");

        Assert.Contains("Available tools: shell_execute", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("## Active skills", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("## Default skills", prompt, StringComparison.Ordinal);
    }
}

file sealed class InMemoryAgenticPolicyCatalogStore : IAgenticPolicyCatalogStore
{
    private readonly List<AgenticSkillDefinition> _skills = AgenticCatalogSeed.Skills.ToList();
    private readonly List<AgenticGuardrailDefinition> _guardrails = AgenticCatalogSeed.Guardrails.ToList();

    public Task EnsureSeededAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<AgenticCatalogSnapshot> GetCatalogAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new AgenticCatalogSnapshot { Skills = _skills, Guardrails = _guardrails });

    public Task<AgenticSkillDefinition?> GetSkillAsync(string id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_skills.FirstOrDefault(s => s.Id == id));

    public Task<AgenticSkillDefinition> UpsertSkillAsync(
        AgenticSkillDefinition skill,
        CancellationToken cancellationToken = default)
    {
        _skills.RemoveAll(s => s.Id == skill.Id);
        _skills.Add(skill);
        return Task.FromResult(skill);
    }

    public Task<bool> DeleteSkillAsync(string id, CancellationToken cancellationToken = default)
    {
        var n = _skills.RemoveAll(s => s.Id == id && !s.IsSystem);
        return Task.FromResult(n > 0);
    }

    public Task<IReadOnlyList<AgenticGuardrailDefinition>> ListGuardrailsAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AgenticGuardrailDefinition>>(_guardrails);

    public Task<AgenticGuardrailDefinition?> GetGuardrailAsync(string id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_guardrails.FirstOrDefault(g => g.Id == id));

    public Task<AgenticGuardrailDefinition> UpsertGuardrailAsync(
        AgenticGuardrailDefinition guardrail,
        CancellationToken cancellationToken = default)
    {
        _guardrails.RemoveAll(g => g.Id == guardrail.Id);
        _guardrails.Add(guardrail);
        return Task.FromResult(guardrail);
    }

    public Task<bool> DeleteGuardrailAsync(string id, CancellationToken cancellationToken = default)
    {
        var n = _guardrails.RemoveAll(g => g.Id == id && !g.IsSystem);
        return Task.FromResult(n > 0);
    }
}

file sealed class InMemoryAgenticAppPolicyCatalogStore : IAgenticAppPolicyCatalogStore
{
    private readonly List<AgenticAppSkillDefinition> _skills = [];
    private readonly List<AgenticAppGuardrailDefinition> _guardrails = [];

    public Task<AgenticAppCatalogSnapshot> GetCatalogAsync(string appId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new AgenticAppCatalogSnapshot
        {
            Skills = _skills.Where(s => s.AppId == appId).ToList(),
            Guardrails = _guardrails.Where(g => g.AppId == appId).ToList()
        });

    public Task<AgenticAppSkillDefinition?> GetSkillAsync(string appId, string id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_skills.FirstOrDefault(s => s.AppId == appId && s.Id == id));

    public Task<AgenticAppSkillDefinition> UpsertSkillAsync(
        AgenticAppSkillDefinition skill,
        CancellationToken cancellationToken = default)
    {
        _skills.RemoveAll(s => s.AppId == skill.AppId && s.Id == skill.Id);
        _skills.Add(skill);
        return Task.FromResult(skill);
    }

    public Task<bool> DeleteSkillAsync(string appId, string id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_skills.RemoveAll(s => s.AppId == appId && s.Id == id) > 0);

    public Task<AgenticAppGuardrailDefinition?> GetGuardrailAsync(
        string appId,
        string id,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_guardrails.FirstOrDefault(g => g.AppId == appId && g.Id == id));

    public Task<AgenticAppGuardrailDefinition> UpsertGuardrailAsync(
        AgenticAppGuardrailDefinition guardrail,
        CancellationToken cancellationToken = default)
    {
        _guardrails.RemoveAll(g => g.AppId == guardrail.AppId && g.Id == guardrail.Id);
        _guardrails.Add(guardrail);
        return Task.FromResult(guardrail);
    }

    public Task<bool> DeleteGuardrailAsync(string appId, string id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_guardrails.RemoveAll(g => g.AppId == appId && g.Id == id) > 0);
}
