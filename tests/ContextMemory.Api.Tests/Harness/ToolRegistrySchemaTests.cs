using ContextMemory.Core.Agentic;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Xunit;

namespace ContextMemory.Api.Tests.Harness;

public sealed class ToolRegistrySchemaTests
{
    [Fact]
    public void BuildBuiltinSchemas_Comes_Only_From_Descriptors()
    {
        var fake = new FakeDescriptor(
            new ToolRegistration("fake_tool", ToolCapabilities.QueryFocused, ToolHandlerKind.AppScoped));
        var registry = new ToolRegistry([fake]);
        var config = new AppRuntimeConfig { AppId = "test" };

        var schemas = registry.BuildBuiltinSchemas(config).ToList();

        Assert.Single(schemas);
        Assert.Equal("fake_tool", schemas[0].Function.Name);
        Assert.DoesNotContain(schemas, t => t.Function.Name.StartsWith("wiki_", StringComparison.Ordinal));
    }

    private sealed class FakeDescriptor(params ToolRegistration[] regs) : IToolDescriptor
    {
        public IEnumerable<ToolRegistration> Describe() => regs;

        public IEnumerable<OllamaTool> BuildSchemas(AppRuntimeConfig runtimeConfig) =>
            regs.Select(r => new OllamaTool(
                "function",
                new OllamaFunction(r.WireName, "test schema", new { type = "object" })));
    }
}
