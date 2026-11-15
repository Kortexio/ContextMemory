using System.Text.RegularExpressions;
using Xunit;

namespace ContextMemory.Api.Tests.Harness;

/// <summary>
/// Product code must not ship NotImplemented / Coming-soon placeholders.
/// Test stubs under tests/ and bench --stub-only are allowed.
/// </summary>
public sealed class NoProductStubsTests
{
    private static readonly Regex Forbidden = new(
        @"NotImplementedException|Coming soon|would run here|\bTODO\b.*\bstub\b|\bFIXME\b.*\bstub\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    [Fact]
    public void Src_Has_No_Product_Stub_Markers()
    {
        var root = FindRepoRoot();
        var src = Path.Combine(root, "src");
        Assert.True(Directory.Exists(src), $"Missing src at {src}");

        var hits = new List<string>();
        foreach (var file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            // Skip auto-generated.
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                continue;

            var text = File.ReadAllText(file);
            foreach (Match m in Forbidden.Matches(text))
                hits.Add($"{Path.GetRelativePath(root, file)}: {m.Value}");
        }

        Assert.True(hits.Count == 0, "Product stub markers found:\n" + string.Join("\n", hits));
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

        throw new InvalidOperationException("Could not locate ContextMemory.sln from test output.");
    }
}
