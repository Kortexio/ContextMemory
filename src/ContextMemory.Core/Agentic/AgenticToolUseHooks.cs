using System.Text.Json;
using System.Text.RegularExpressions;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

/// <summary>Pre/PostToolUse hooks driven by guardrail catalog entries.</summary>
public static class AgenticToolUseHooks
{
    public sealed record HookDecision(bool Allowed, string? Message, bool RequireConfirm);

    public static HookDecision EvaluatePreToolUse(
        string toolName,
        string? arguments,
        AppRuntimeConfig runtimeConfig)
    {
        foreach (var guardrail in runtimeConfig.ResolvedPolicy.ActiveGuardrails
                     .Where(g => string.Equals(g.Kind, AgenticGuardrailKinds.PreToolUse, StringComparison.OrdinalIgnoreCase)))
        {
            var cfg = ParseConfig(guardrail.ConfigJson);
            if (!MatchesTool(toolName, cfg))
                continue;

            if (cfg.DenyToolPatterns.Count > 0 && MatchesAny(toolName, cfg.DenyToolPatterns))
            {
                return new HookDecision(
                    false,
                    $"PreToolUse hook `{guardrail.Id}` denied tool `{toolName}`.",
                    RequireConfirm: false);
            }

            if (cfg.AllowToolPatterns.Count > 0 && !MatchesAny(toolName, cfg.AllowToolPatterns))
            {
                return new HookDecision(
                    false,
                    $"PreToolUse hook `{guardrail.Id}` does not allow tool `{toolName}`.",
                    RequireConfirm: false);
            }

            if (cfg.RequireConfirm)
            {
                return new HookDecision(
                    true,
                    $"PreToolUse hook `{guardrail.Id}` requires confirmation for `{toolName}`.",
                    RequireConfirm: true);
            }
        }

        _ = arguments;
        return new HookDecision(true, null, false);
    }

    public static string ApplyPostToolUse(
        string toolName,
        string output,
        AppRuntimeConfig runtimeConfig)
    {
        var result = output ?? string.Empty;
        foreach (var guardrail in runtimeConfig.ResolvedPolicy.ActiveGuardrails
                     .Where(g => string.Equals(g.Kind, AgenticGuardrailKinds.PostToolUse, StringComparison.OrdinalIgnoreCase)))
        {
            var cfg = ParseConfig(guardrail.ConfigJson);
            if (!MatchesTool(toolName, cfg))
                continue;

            foreach (var pattern in cfg.RedactPatterns)
            {
                try
                {
                    result = Regex.Replace(result, pattern, "[redacted]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                }
                catch
                {
                    // ignore invalid patterns
                }
            }

            if (cfg.MaxOutputChars > 0 && result.Length > cfg.MaxOutputChars)
                result = result[..cfg.MaxOutputChars] + "\n…[post-tool-use truncated]";
        }

        return result;
    }

    private static bool MatchesTool(string toolName, HookConfig cfg)
    {
        if (cfg.MatchToolPatterns.Count == 0)
            return true;
        return MatchesAny(toolName, cfg.MatchToolPatterns);
    }

    private static bool MatchesAny(string toolName, IReadOnlyList<string> patterns)
    {
        foreach (var p in patterns)
        {
            if (string.IsNullOrWhiteSpace(p))
                continue;
            if (p == "*")
                return true;
            if (toolName.Contains(p, StringComparison.OrdinalIgnoreCase))
                return true;
            try
            {
                if (Regex.IsMatch(toolName, p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                    return true;
            }
            catch
            {
                // ignore
            }
        }

        return false;
    }

    private static HookConfig ParseConfig(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new HookConfig();
        try
        {
            return JsonSerializer.Deserialize<HookConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new HookConfig();
        }
        catch
        {
            return new HookConfig();
        }
    }

    private sealed class HookConfig
    {
        public List<string> MatchToolPatterns { get; set; } = [];
        public List<string> DenyToolPatterns { get; set; } = [];
        public List<string> AllowToolPatterns { get; set; } = [];
        public List<string> RedactPatterns { get; set; } = [];
        public bool RequireConfirm { get; set; }
        public int MaxOutputChars { get; set; }
    }
}
