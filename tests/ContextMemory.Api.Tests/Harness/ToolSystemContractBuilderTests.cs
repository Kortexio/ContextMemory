using ContextMemory.Core.Agentic.Harness;
using ContextMemory.Core.Contracts;
using Xunit;

namespace ContextMemory.Api.Tests;

public sealed class ToolSystemContractBuilderTests
{
    [Fact]
    public void Build_Uses_Capabilities_Not_Hardcoded_Name_Lists()
    {
        var contract = ToolSystemContractBuilder.Build(
        [
            ("wiki_search", ToolCapabilities.QueryFocused),
            ("shell_execute", ToolCapabilities.Sandbox)
        ]);

        Assert.Contains("### Tool contract", contract, StringComparison.Ordinal);
        Assert.Contains("wiki_search", contract, StringComparison.Ordinal);
        Assert.Contains("Discovery:", contract, StringComparison.Ordinal);
        Assert.Contains("Execution: shell_execute", contract, StringComparison.Ordinal);
        Assert.Contains(
            "Schemas for listed tools are already in the catalog",
            contract,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Build_Http_And_Browser_Sections_From_Caps()
    {
        var contract = ToolSystemContractBuilder.Build(
        [
            ("fetch_url", ToolCapabilities.Http),
            ("browser_navigate", ToolCapabilities.Browser)
        ]);

        Assert.Contains("HTTP:", contract, StringComparison.Ordinal);
        Assert.Contains("Browser:", contract, StringComparison.Ordinal);
    }
}
