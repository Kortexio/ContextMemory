using System.Text.RegularExpressions;
using Xunit;

namespace ContextMemory.Api.Tests.Harness;

/// <summary>
/// Guards against reintroducing orchestration-side tool name lists.
/// </summary>
public sealed class NoHardcodedToolNameListsTests
{
    private static readonly Regex Forbidden = new(
        @"IsDiscoveryTool|QueryFocusedTools|IsSandboxTool\s*\(|StartsWith\s*\(\s*""wiki_",
        RegexOptions.Compiled);

    [Fact]
    public void Core_Agentic_Orchestration_Has_No_Forbidden_Name_List_Helpers()
    {
        var root = FindRepoRoot();
        var dirs = new[]
        {
            Path.Combine(root, "src", "ContextMemory.Core", "Agentic", "Loop"),
            Path.Combine(root, "src", "ContextMemory.Core", "Agentic", "Guardrails"),
            Path.Combine(root, "src", "ContextMemory.Core", "Agentic", "Harness"),
            Path.Combine(root, "src", "ContextMemory.Core", "Agentic", "Validation"),
            Path.Combine(root, "src", "ContextMemory.Core", "Agentic", "Tools", "AgenticToolRegistryService.cs")
        };

        var hits = new List<string>();
        foreach (var path in dirs)
        {
            if (File.Exists(path))
            {
                ScanFile(path, hits);
                continue;
            }

            if (!Directory.Exists(path))
                continue;

            foreach (var file in Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories))
                ScanFile(file, hits);
        }

        Assert.True(hits.Count == 0, "Forbidden patterns:\n" + string.Join("\n", hits));
    }

    private static void ScanFile(string file, List<string> hits)
    {
        var text = File.ReadAllText(file);
        foreach (Match m in Forbidden.Matches(text))
            hits.Add($"{file}: {m.Value}");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ContextMemory.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate ContextMemory.sln");
    }
}
