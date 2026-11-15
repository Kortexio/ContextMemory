using ContextMemory.Core.Agentic;
using ContextMemory.Core.Agentic.Harness;
using ContextMemory.Core.Agentic.Harness.Families;
using ContextMemory.Core.Models;
using Xunit;

namespace ContextMemory.Api.Tests;

public sealed class ToolSystemContractBuilderTests
{
    [Fact]
    public void Build_IncludesWikiSearch_ContractLine_WhenActive()
    {
        var contract = ToolSystemContractBuilder.Build(["wiki_search", "shell_execute"]);

        Assert.Contains("### Tool contract", contract, StringComparison.Ordinal);
        Assert.Contains("wiki_search", contract, StringComparison.Ordinal);
        Assert.Contains("Discovery:", contract, StringComparison.Ordinal);
        Assert.Contains("Execution: shell_execute", contract, StringComparison.Ordinal);
        Assert.Contains(
            "Schemas for listed tools are already in the catalog",
            contract,
            StringComparison.Ordinal);
    }
}
