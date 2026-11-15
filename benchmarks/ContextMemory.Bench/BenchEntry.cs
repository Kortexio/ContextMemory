using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using ContextMemory.Core.Agentic;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace ContextMemory.Bench;

internal static class BenchEntry
{
    public static async Task<int> Main(string[] args)
    {

        var baseUrlEnv = (Environment.GetEnvironmentVariable("CM_BENCH_BASE_URL") ?? "").Trim();
        if (string.IsNullOrWhiteSpace(baseUrlEnv))
        {
            Console.Error.WriteLine("Set CM_BENCH_BASE_URL to an OpenAI-compatible /v1 host before running live A/B.");
            return 2;
        }
        var baseUrl = baseUrlEnv.TrimEnd('/');
        var model = Environment.GetEnvironmentVariable("CM_BENCH_MODEL") ?? "qwen3.6-35b-iq3s-mtp";
        var apiKey = (Environment.GetEnvironmentVariable("CM_BENCH_API_KEY")
            ?? Environment.GetEnvironmentVariable("CM_BENCH_BEARER")
            ?? "").Trim();
        var label = GetArg("--label") ?? "ab";
        var tasksRoot = GetArg("--tasks") ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "tasks"));
        var resultsDir = GetArg("--out") ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "results"));
        var only = GetArg("--only"); // optional comma-separated task ids

        if (!Directory.Exists(tasksRoot))
        {
            // Fallback: repo-relative when running via `dotnet run --project benchmarks/ContextMemory.Bench`
            tasksRoot = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "benchmarks", "tasks"));
            resultsDir = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "benchmarks", "results"));
        }

        Console.WriteLine($"CM bench A/B");
        Console.WriteLine($"  LLM:   {baseUrl}  model={model}");
        Console.WriteLine($"  Tasks: {tasksRoot}");
        Console.WriteLine($"  Auth:  {(string.IsNullOrEmpty(apiKey) ? "(none)" : "(set)")}");

        var taskDirs = Directory.GetDirectories(tasksRoot)
            .Select(d => Path.Combine(d, "task.json"))
            .Where(File.Exists)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (taskDirs.Count == 0)
        {
            Console.Error.WriteLine("No tasks found.");
            return 1;
        }

        var onlySet = string.IsNullOrWhiteSpace(only)
            ? null
            : only.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var dataRoot = Path.Combine(Path.GetTempPath(), "cm-bench", Guid.NewGuid().ToString("N"));
        await using var factory = await BenchHost.StartAsync(dataRoot, baseUrl, model, string.IsNullOrEmpty(apiKey) ? null : apiKey);
        
        const string appId = "bench-app";
        using (var scope = factory.Services.CreateScope())
        {
            var configStore = scope.ServiceProvider.GetRequiredService<IAppConfigStore>();
            configStore.EnsureProfileExists(appId);
        }

        var stamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH-mm-ss-fffZ");
        var runId = $"{label}-{stamp}";
        var taskRows = new JsonArray();

        foreach (var taskPath in taskDirs)
        {
            using var taskDoc = JsonDocument.Parse(await File.ReadAllTextAsync(taskPath));
            var task = taskDoc.RootElement.Clone();
            var id = task.GetProperty("id").GetString() ?? Path.GetFileName(Path.GetDirectoryName(taskPath))!;
            if (onlySet is not null && !onlySet.Contains(id))
                continue;

            Console.WriteLine();
            Console.WriteLine($"=== {id} ===");

            var fixturesRoot = Path.GetDirectoryName(tasksRoot)!; // benchmarks/
            await SeedWikiAsync(factory, appId, task, fixturesRoot);

            var off = await RunOneAsync(factory, appId, model, task, harnessMode: "off");
            var on = await RunOneAsync(factory, appId, model, task, harnessMode: "weak");

            Console.WriteLine($"  off score={off.Score} pass={off.Pass} falseDone={off.FalseDone} stuck={off.Stuck} badArgs={off.BadArgs} lifts={off.FamilyLifts}");
            Console.WriteLine($"  on  score={on.Score} pass={on.Pass} falseDone={on.FalseDone} stuck={on.Stuck} badArgs={on.BadArgs} lifts={on.FamilyLifts}");

            taskRows.Add(new JsonObject
            {
                ["id"] = id,
                ["expectBaselineFail"] = task.TryGetProperty("expectBaselineFail", out var ebf) && ebf.GetBoolean(),
                ["prompt"] = task.TryGetProperty("prompt", out var pr) ? pr.GetString() : null,
                ["off"] = MetricsToJson(off, "off"),
                ["on"] = MetricsToJson(on, "on")
            });
        }

        Directory.CreateDirectory(resultsDir);
        var meanOff = Mean(taskRows, "off");
        var meanOn = Mean(taskRows, "on");
        var doc = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["kind"] = "cm-bench-ab",
            ["runId"] = runId,
            ["createdAt"] = DateTimeOffset.UtcNow.ToString("O"),
            ["reachable"] = true,
            // Never persist the live LLM host — results may be shared or committed by mistake.
            ["endpoint"] = "(redacted)/v1/chat/completions",
            ["model"] = model,
            ["env"] = new JsonObject
            {
                ["CM_BENCH_BASE_URL"] = "(redacted)",
                ["CM_BENCH_MODEL"] = model,
                ["CM_BENCH_API_KEY"] = string.IsNullOrEmpty(apiKey) ? "(none — target API open)" : "(set — target API auth)"
            },
            ["protocol"] = new JsonObject
            {
                ["description"] = "Live A/B: harness off vs weak (in-process CM) against OpenAI-compatible /v1.",
                ["expectBaselineFail"] = true
            },
            ["tasks"] = taskRows,
            ["summary"] = new JsonObject
            {
                ["meanScoreOff"] = meanOff,
                ["meanScoreOn"] = meanOn,
                ["deltaScore"] = meanOff is null || meanOn is null ? null : Math.Round(meanOn.Value - meanOff.Value, 1),
                ["stuckOff"] = CountBool(taskRows, "off", "stuck"),
                ["stuckOn"] = CountBool(taskRows, "on", "stuck"),
                ["badArgsOff"] = CountBool(taskRows, "off", "badArgs"),
                ["badArgsOn"] = CountBool(taskRows, "on", "badArgs"),
                ["falseDoneOff"] = CountBool(taskRows, "off", "falseDone"),
                ["falseDoneOn"] = CountBool(taskRows, "on", "falseDone"),
                ["note"] = "Live in-process A/B: Off = client-side wire without recovery; On = weak harness."
            }
        };

        var outPath = Path.Combine(resultsDir, $"{runId}.json");
        await File.WriteAllTextAsync(outPath, doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        await File.WriteAllTextAsync(
            Path.Combine(resultsDir, "latest.json"),
            JsonSerializer.Serialize(new { path = outPath, runId }, new JsonSerializerOptions { WriteIndented = true }) + "\n");

        Console.WriteLine();
        Console.WriteLine($"Wrote {outPath}");
        Console.WriteLine($"Summary mean: off={meanOff?.ToString() ?? "—"} on={meanOn?.ToString() ?? "—"} Δ={(meanOff is null || meanOn is null ? "—" : (meanOn.Value - meanOff.Value).ToString("0.0"))}");
        return 0;

        static string? GetArg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length; i++)
            {
                if (!args[i].Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
                if (i + 1 < args.Length) return args[i + 1];
                return "true";
            }
            return null;
        }

        static async Task SeedWikiAsync(BenchHost factory, string appId, JsonElement task, string fixturesRoot)
        {
            if (!task.TryGetProperty("fixtures", out var fixtures) || fixtures.ValueKind != JsonValueKind.Array)
                return;

            using var scope = factory.Services.CreateScope();
            var wiki = scope.ServiceProvider.GetRequiredService<IGlobalWikiStore>();
            foreach (var item in fixtures.EnumerateArray())
            {
                var rel = item.GetString();
                if (string.IsNullOrWhiteSpace(rel) || !rel.Contains("wiki/", StringComparison.OrdinalIgnoreCase))
                    continue;
                var abs = Path.IsPathRooted(rel) ? rel : Path.GetFullPath(Path.Combine(fixturesRoot, rel));
                if (!File.Exists(abs))
                {
                    Console.WriteLine($"  warn: fixture missing {rel}");
                    continue;
                }
                var content = await File.ReadAllTextAsync(abs);
                var docId = Path.GetFileNameWithoutExtension(abs);
                var title = content.Split('\n').FirstOrDefault(l => l.StartsWith("# "))?.TrimStart('#', ' ').Trim()
                    ?? docId;
                await wiki.UpsertAsync(appId, docId, new GlobalWikiUpsertRequest
                {
                    Title = title,
                    Content = content,
                    SourceId = "cm-bench",
                    Overwrite = true
                });
                Console.WriteLine($"  seeded wiki:{docId}");
            }
        }

        static async Task<TaskScorer.Metrics> RunOneAsync(
            BenchHost factory,
            string appId,
            string model,
            JsonElement task,
            string harnessMode)
        {
            using var scope = factory.Services.CreateScope();
            var configStore = scope.ServiceProvider.GetRequiredService<IAppConfigStore>();
            var orchestrator = scope.ServiceProvider.GetRequiredService<IAgentOrchestrator>();

            var endpoint = (Environment.GetEnvironmentVariable("CM_BENCH_BASE_URL") ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrWhiteSpace(endpoint))
                throw new InvalidOperationException("CM_BENCH_BASE_URL is required.");
            var key = (Environment.GetEnvironmentVariable("CM_BENCH_API_KEY")
                ?? Environment.GetEnvironmentVariable("CM_BENCH_BEARER")
                ?? "").Trim();

            var taskId = task.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
            var stickyWiki = taskId.StartsWith("CM4", StringComparison.OrdinalIgnoreCase);

            // CM4: no MCP (pivot must stay on wiki). Others: mock CRM unlocks agentic + CM2.
            var tools = stickyWiki
                ? new AgenticToolsConfig { Http = new AgenticHttpToolsConfig { Enabled = true, AllowedHosts = ["example.invalid"] } }
                : new AgenticToolsConfig
                {
                    Integrations =
                    [
                        new IntegrationToolConfig
                        {
                            Type = "mcp",
                            Name = "bench-crm",
                            Url = "mock://crm",
                            AuthMode = "none",
                            Description = "Bench CRM mock with query_objects"
                        }
                    ]
                };

            var runtime = await configStore.UpdateAsync(appId, new AppConfigPatchRequest
            {
                GlobalWikiEnabled = true,
                LlmModel = model,
                LlmBackend = "openai-compatible",
                LlmEndpoint = endpoint,
                LlmApiKey = string.IsNullOrEmpty(key) ? null : key,
                LlmThinkEnabled = true,
                Agentic = new AgenticConfig
                {
                    Enabled = true,
                    HarnessMode = harnessMode,
                    PromptProfile = "qwen",
                    Tools = tools,
                    Guardrails = new AgenticGuardrailsConfig
                    {
                        MaxIterations = stickyWiki ? 16 : 12,
                        LoopTimeoutSeconds = 300,
                        ValidationMode = "deterministic"
                    }
                }
            });

            var prompt = task.GetProperty("prompt").GetString() ?? "";
            var sessionId = $"bench-{harnessMode}-{Guid.NewGuid():N}";
            var request = new OllamaRequest
            {
                Model = model,
                Stream = false,
                Messages = [new OllamaMessage { Role = "user", Content = prompt }]
            };

            if (stickyWiki)
                factory.StickyWiki.BeginSession(sessionId);

            Console.WriteLine($"  run harness={harnessMode}{(stickyWiki ? " stickyWiki=on" : "")} …");
            var sw = Stopwatch.StartNew();
            AgentResult result;
            try
            {
                result = await orchestrator.RunAsync(appId, "bench-user", sessionId, request, runtime);
            }
            catch (Exception ex)
            {
                sw.Stop();
                if (stickyWiki) factory.StickyWiki.EndSession();
                Console.WriteLine($"  error harness={harnessMode}: {ex.Message}");
                return new TaskScorer.Metrics(
                    Score: 0,
                    Pass: false,
                    FalseDone: true,
                    Stuck: false,
                    BadArgs: false,
                    FamilyLifts: 0,
                    Repairs: 0,
                    LoopExit: "error",
                    Steps: 0,
                    LatencyMs: sw.ElapsedMilliseconds,
                    FailureClass: "A-harness",
                    Note: ex.Message);
            }
            finally
            {
                if (stickyWiki) factory.StickyWiki.EndSession();
            }

            sw.Stop();
            var metrics = TaskScorer.Score(task, result, sw.ElapsedMilliseconds);
            Console.WriteLine($"    answer: {Truncate(result.FinalAnswer, 160)}");
            Console.WriteLine($"    steps={result.Steps.Count} iters={result.Iterations} success={result.Success} stuck={metrics.Stuck} ms={sw.ElapsedMilliseconds}");
            if (result.Steps.Count > 0)
            {
                foreach (var step in result.Steps.Take(8))
                    Console.WriteLine($"      · {step.ToolName} ok={step.Success} args={Truncate(step.Arguments, 60)} out={Truncate(step.Output, 50)}");
            }
            return metrics;
        }

        static JsonObject MetricsToJson(TaskScorer.Metrics m, string harness) => new()
        {
            ["harness"] = harness,
            ["score"] = m.Score,
            ["pass"] = m.Pass,
            ["falseDone"] = m.FalseDone,
            ["stuck"] = m.Stuck,
            ["badArgs"] = m.BadArgs,
            ["familyLifts"] = m.FamilyLifts,
            ["repairs"] = m.Repairs,
            ["loopExit"] = m.LoopExit,
            ["steps"] = m.Steps,
            ["latencyMs"] = m.LatencyMs,
            ["failureClass"] = m.FailureClass,
            ["note"] = m.Note
        };

        static double? Mean(JsonArray rows, string side)
        {
            var vals = new List<double>();
            foreach (var row in rows)
            {
                var score = row?[side]?["score"];
                if (score is JsonValue jv && jv.TryGetValue<double>(out var d))
                    vals.Add(d);
            }
            return vals.Count == 0 ? null : Math.Round(vals.Average(), 1);
        }

        static int? CountBool(JsonArray rows, string side, string prop)
        {
            var n = 0;
            var any = false;
            foreach (var row in rows)
            {
                var node = row?[side]?[prop];
                if (node is JsonValue jv && jv.TryGetValue<bool>(out var b))
                {
                    any = true;
                    if (b) n++;
                }
            }
            return any ? n : null;
        }

        static string Truncate(string? s, int n)
        {
            if (string.IsNullOrEmpty(s)) return "(empty)";
            s = s.Replace('\n', ' ').Trim();
            return s.Length <= n ? s : s[..n] + "…";
        }
    }
}
