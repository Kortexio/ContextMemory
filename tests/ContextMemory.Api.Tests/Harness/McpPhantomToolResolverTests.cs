using ContextMemory.Core.Agentic.Mcp;
using Xunit;

namespace ContextMemory.Api.Tests.Harness;

public sealed class McpPhantomToolResolverTests
{
    [Fact]
    public void Resolve_RemapsLiteralServerPrefix_WhenShortNameUnique()
    {
        var catalog = new[] { "bench-crm__query_objects", "bench-crm__get_account", "wiki_search" };
        var resolved = McpPhantomToolResolver.Resolve("server__query_objects", catalog);
        Assert.Equal("bench-crm__query_objects", resolved);
    }

    [Fact]
    public void Resolve_ReturnsNull_ForInventedShortName()
    {
        var catalog = new[] { "bench-crm__query_objects" };
        Assert.Null(McpPhantomToolResolver.Resolve("server__nope", catalog));
        Assert.Null(McpPhantomToolResolver.Resolve("zuora__invented", catalog));
    }

    [Fact]
    public void Resolve_ExpandsShortName_WhenUnambiguous()
    {
        var catalog = new[] { "bench-crm__query_objects", "wiki_search" };
        Assert.Equal("bench-crm__query_objects", McpPhantomToolResolver.Resolve("query_objects", catalog));
    }
}
