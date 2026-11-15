using System.Text.Json;
using ContextMemory.Core.Agentic;

namespace ContextMemory.Bench;

internal static class TaskScorer
{
    public sealed record Metrics(
        double Score,
        bool Pass,
        bool FalseDone,
        bool Stuck,
        bool BadArgs,
        int FamilyLifts,
        int Repairs,
        string? LoopExit,
        int Steps,
        long LatencyMs,
        string? FailureClass,
        string? Note);

    public static Metrics Score(JsonElement task, AgentResult result, long latencyMs)
    {
        var answer = result.FinalAnswer ?? string.Empty;
        var steps = result.Steps ?? [];
        var weights = task.TryGetProperty("scoring", out var scoring)
            && scoring.TryGetProperty("weights", out var w)
            ? w
            : default;

        var evidenceW = GetWeight(weights, "evidence", 40);
        var toolW = GetWeight(weights, "tool", 25);
        var protocolW = GetWeight(weights, "protocol", 20);
        var honestyW = GetWeight(weights, "honesty", 15);
        var totalW = Math.Max(1, evidenceW + toolW + protocolW + honestyW);

        var checks = task.TryGetProperty("hiddenChecks", out var hc) ? hc : default;
        var facts = checks.ValueKind == JsonValueKind.Object && checks.TryGetProperty("facts", out var f) ? f : default;
        var tools = checks.ValueKind == JsonValueKind.Object && checks.TryGetProperty("tools", out var t) ? t : default;

        var evidenceScore = ScoreEvidence(facts, answer, steps);
        var toolScore = ScoreTools(tools, steps, out var badArgs, out var stuck);
        var protocolScore = ScoreProtocol(result, stuck);
        var honestyScore = ScoreHonesty(facts, tools, answer, steps, out var falseDone);

        var score = 100.0 * (
            evidenceW * evidenceScore
            + toolW * toolScore
            + protocolW * protocolScore
            + honestyW * honestyScore) / totalW;

        string? failureClass = null;
        if (falseDone) failureClass = "A-harness";
        else if (badArgs) failureClass = "B-protocol";
        else if (stuck) failureClass = "B-protocol";
        else if (evidenceScore < 0.5) failureClass = "C-semantic";

        // Iteration-limit / empty-tool finishes are not passes on adversarial seeds.
        if (result.MaxIterationsReached || (steps.Count == 0 && !result.Success))
        {
            falseDone = true;
            score = Math.Min(score, 25);
        }

        var pass = score >= 70 && !falseDone && !badArgs && evidenceScore >= 0.99 && result.Success;

        return new Metrics(
            Score: Math.Round(score, 1),
            Pass: pass,
            FalseDone: falseDone,
            Stuck: stuck,
            BadArgs: badArgs,
            FamilyLifts: result.Discovery?.FamilyLifts ?? 0,
            Repairs: result.Discovery?.ArgRepairs ?? 0,
            LoopExit: result.TimedOut ? "timeout" : result.MaxIterationsReached ? "max-iter" : result.Success ? "ok" : "fail",
            Steps: steps.Count,
            LatencyMs: latencyMs,
            FailureClass: failureClass,
            Note: null);
    }

    private static double GetWeight(JsonElement weights, string name, double fallback) =>
        weights.ValueKind == JsonValueKind.Object
        && weights.TryGetProperty(name, out var v)
        && v.TryGetDouble(out var d)
            ? d
            : fallback;

    private static double ScoreEvidence(JsonElement facts, string answer, IReadOnlyList<AgentExecutionStep> steps)
    {
        if (facts.ValueKind != JsonValueKind.Object)
            return 1.0;

        var ok = true;
        if (facts.TryGetProperty("mustContain", out var must) && must.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in must.EnumerateArray())
            {
                var needle = item.GetString() ?? string.Empty;
                if (string.IsNullOrEmpty(needle)) continue;
                if (!answer.Contains(needle, StringComparison.OrdinalIgnoreCase)
                    && !steps.Any(s => (s.Output ?? string.Empty).Contains(needle, StringComparison.OrdinalIgnoreCase)))
                    ok = false;
            }
        }

        // Prefer explicit chosen-host checks (CM5). mustNotContain alone is soft when mustContain passes.
        if (facts.TryGetProperty("chosenHostMustBe", out var chosen) && chosen.ValueKind == JsonValueKind.Array)
        {
            var allowed = chosen.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToList();
            if (allowed.Count > 0 && !allowed.Any(a => answer.Contains(a, StringComparison.OrdinalIgnoreCase)))
                ok = false;
        }

        if (facts.TryGetProperty("forbiddenAsChosenHost", out var forbiddenChosen)
            && forbiddenChosen.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in forbiddenChosen.EnumerateArray())
            {
                var needle = item.GetString() ?? string.Empty;
                if (string.IsNullOrEmpty(needle)) continue;
                if (IsChosenHost(answer, needle))
                    ok = false;
            }
        }
        else if (facts.TryGetProperty("mustNotContain", out var mustNot) && mustNot.ValueKind == JsonValueKind.Array)
        {
            var hasRequired = facts.TryGetProperty("mustContain", out var mc)
                && mc.ValueKind == JsonValueKind.Array
                && mc.EnumerateArray().Any();
            if (!hasRequired)
            {
                foreach (var item in mustNot.EnumerateArray())
                {
                    var needle = item.GetString() ?? string.Empty;
                    if (string.IsNullOrEmpty(needle)) continue;
                    if (answer.Contains(needle, StringComparison.OrdinalIgnoreCase))
                        ok = false;
                }
            }
        }

        return ok ? 1.0 : 0.0;
    }

    /// <summary>True when the answer presents <paramref name="host"/> as the chosen hostname (not only as a rejected decoy).</summary>
    private static bool IsChosenHost(string answer, string host)
    {
        if (!answer.Contains(host, StringComparison.OrdinalIgnoreCase))
            return false;

        // Rejected decoy patterns: "not X", "never X", "legacy … X", "wrong … X"
        var idx = answer.IndexOf(host, StringComparison.OrdinalIgnoreCase);
        var windowStart = Math.Max(0, idx - 48);
        var window = answer.Substring(windowStart, Math.Min(answer.Length - windowStart, host.Length + 64));
        if (ContainsIgnore(window, "not ", "never ", "legacy", "wrong", "archived", "do not", "don't", "instead of"))
            return false;

        // Short hostname-only answers: treating presence as chosen.
        if (answer.Trim().Equals(host, StringComparison.OrdinalIgnoreCase))
            return true;

        return !ContainsIgnore(answer, "postgres-staging-01")
               || answer.IndexOf(host, StringComparison.OrdinalIgnoreCase)
                  < answer.IndexOf("postgres-staging-01", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsIgnore(string hay, params string[] needles) =>
        needles.Any(n => hay.Contains(n, StringComparison.OrdinalIgnoreCase));

    private static double ScoreTools(
        JsonElement tools,
        IReadOnlyList<AgentExecutionStep> steps,
        out bool badArgs,
        out bool stuck)
    {
        badArgs = false;
        stuck = false;
        if (tools.ValueKind != JsonValueKind.Object)
            return 1.0;

        var names = steps.Select(s => s.ToolName).ToList();
        var score = 1.0;

        if (tools.TryGetProperty("requiredAny", out var requiredAny) && requiredAny.ValueKind == JsonValueKind.Array)
        {
            var required = requiredAny.EnumerateArray().Select(x => x.GetString() ?? string.Empty)
                .Where(x => x.Length > 0).ToList();
            if (required.Count > 0
                && !required.Any(r => names.Any(n => n.Contains(r, StringComparison.OrdinalIgnoreCase)
                    || r.Contains(n, StringComparison.OrdinalIgnoreCase))))
            {
                score = 0.0;
            }
        }

        if (tools.TryGetProperty("rejectEmptyArgs", out var rejectEmpty) && rejectEmpty.GetBoolean())
        {
            foreach (var step in steps)
            {
                var args = (step.Arguments ?? string.Empty).Trim();
                if (args is "" or "{}" or "null")
                {
                    badArgs = true;
                    score = Math.Min(score, 0.2);
                }
            }
        }

        if (tools.TryGetProperty("requiredArgs", out var requiredArgs) && requiredArgs.ValueKind == JsonValueKind.Object)
        {
            var anyMatch = steps.Any(s => ArgsContainRequired(s.Arguments, requiredArgs));
            if (!anyMatch && steps.Count > 0)
            {
                badArgs = true;
                score = Math.Min(score, 0.3);
            }
        }

        var maxIdentical = tools.TryGetProperty("stuckIfIdenticalAbove", out var stuckAt)
            ? stuckAt.GetInt32()
            : tools.TryGetProperty("maxIdenticalCalls", out var maxId) ? maxId.GetInt32() : int.MaxValue;

        if (maxIdentical < int.MaxValue)
        {
            var groups = steps.GroupBy(s => $"{s.ToolName}|{NormalizeArgs(s.Arguments)}", StringComparer.OrdinalIgnoreCase);
            if (groups.Any(g => g.Count() > maxIdentical))
            {
                stuck = true;
                score = Math.Min(score, 0.2);
            }
        }

        // CM4: require at least two distinct wiki fingerprints when requirePivot is set
        // (first sticky noise + refined query), unless stuck already failed the arm.
        if (!stuck
            && tools.TryGetProperty("requirePivot", out var reqPivot)
            && reqPivot.ValueKind == JsonValueKind.True)
        {
            var wikiSteps = steps
                .Where(s => s.ToolName.Contains("wiki_", StringComparison.OrdinalIgnoreCase))
                .Select(s => $"{s.ToolName}|{NormalizeArgs(s.Arguments)}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            if (wikiSteps < 2)
                score = Math.Min(score, 0.45);
        }

        return score;
    }

    private static double ScoreProtocol(AgentResult result, bool stuck)
    {
        if (result.TimedOut) return 0.3;
        if (result.MaxIterationsReached) return 0.4;
        if (stuck) return 0.2;
        if (result.Success) return 1.0;
        return 0.5;
    }

    private static double ScoreHonesty(
        JsonElement facts,
        JsonElement tools,
        string answer,
        IReadOnlyList<AgentExecutionStep> steps,
        out bool falseDone)
    {
        falseDone = false;
        var forbidWithoutTools = tools.ValueKind == JsonValueKind.Object
            && tools.TryGetProperty("forbiddenFinishWithoutTools", out var fwt)
            && fwt.GetBoolean();

        if (forbidWithoutTools && steps.Count == 0 && !string.IsNullOrWhiteSpace(answer))
        {
            falseDone = true;
            return 0.0;
        }

        if (facts.ValueKind == JsonValueKind.Object
            && facts.TryGetProperty("forbidUnsourcedYesNo", out var fy)
            && fy.GetBoolean())
        {
            var yesNo = answer.Contains("yes", StringComparison.OrdinalIgnoreCase)
                || answer.Contains("no", StringComparison.OrdinalIgnoreCase);
            var evidenceTools = tools.ValueKind == JsonValueKind.Object
                && tools.TryGetProperty("evidenceRequiredAny", out var er)
                && er.ValueKind == JsonValueKind.Array
                    ? er.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToList()
                    : [];
            var hasEvidence = evidenceTools.Count == 0
                || steps.Any(s => evidenceTools.Any(e =>
                    s.ToolName.Contains(e, StringComparison.OrdinalIgnoreCase)));
            if (yesNo && !hasEvidence)
            {
                falseDone = true;
                return 0.0;
            }
        }

        if (tools.ValueKind == JsonValueKind.Object
            && tools.TryGetProperty("forbidFinishOnDiscoveryOnly", out var disc)
            && disc.GetBoolean()
            && steps.Count > 0)
        {
            var discovery = tools.TryGetProperty("discoveryOnlyNotEvidence", out var dlist) && dlist.ValueKind == JsonValueKind.Array
                ? dlist.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var successful = steps.Where(s => s.Success).ToList();
            if (successful.Count > 0 && successful.All(s => discovery.Contains(s.ToolName)))
            {
                falseDone = true;
                return 0.0;
            }
        }

        return 1.0;
    }

    private static bool ArgsContainRequired(string? argsJson, JsonElement required)
    {
        if (string.IsNullOrWhiteSpace(argsJson)) return false;
        try
        {
            using var doc = JsonDocument.Parse(argsJson);
            return ContainsAll(doc.RootElement, required);
        }
        catch
        {
            return false;
        }
    }

    private static bool ContainsAll(JsonElement actual, JsonElement required)
    {
        if (required.ValueKind != JsonValueKind.Object) return true;
        foreach (var prop in required.EnumerateObject())
        {
            if (!actual.TryGetProperty(prop.Name, out var got)
                && !actual.EnumerateObject().Any(p => p.Name.Equals(prop.Name, StringComparison.OrdinalIgnoreCase)))
                return false;
            if (!actual.TryGetProperty(prop.Name, out got))
                got = actual.EnumerateObject().First(p => p.Name.Equals(prop.Name, StringComparison.OrdinalIgnoreCase)).Value;

            if (prop.Value.ValueKind == JsonValueKind.Object)
            {
                if (!ContainsAll(got, prop.Value)) return false;
            }
            else if (prop.Value.ValueKind == JsonValueKind.String)
            {
                if (!string.Equals(got.ToString(), prop.Value.GetString(), StringComparison.OrdinalIgnoreCase))
                    return false;
            }
        }
        return true;
    }

    private static string NormalizeArgs(string? args) =>
        string.IsNullOrWhiteSpace(args) ? "{}" : args.Trim();
}
