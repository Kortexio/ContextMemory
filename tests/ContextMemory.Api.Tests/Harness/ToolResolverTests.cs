using ContextMemory.Core.Agentic;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Xunit;

namespace ContextMemory.Api.Tests.Harness;

public sealed class ToolResolverTests
{
    [Fact]
    public void Resolves_Described_BuiltIn_By_WireName()
    {
        try
        {
            var descriptor = new FakeDescriptor(
                new ToolRegistration("fake_tool", ToolCapabilities.QueryFocused, ToolHandlerKind.AppScoped));
            var resolver = new ToolResolver([descriptor]);

            Assert.True(resolver.TryResolve("fake_tool", EmptyConfig(), out var tool));
            Assert.Equal("fake_tool", tool.WireName);
            Assert.True(tool.Capabilities.HasFlag(ToolCapabilities.QueryFocused));
            Assert.False(tool.IsMcp);
            Assert.True(resolver.HasCapability("FAKE_TOOL", ToolCapabilities.QueryFocused));
        }
        finally
        {
            ToolCapabilityTestSeed.PublishBaseline();
        }
    }

    [Fact]
    public void Unknown_WireName_Does_Not_Resolve()
    {
        try
        {
            var resolver = new ToolResolver([]);
            Assert.False(resolver.TryResolve("no_such_tool", EmptyConfig(), out _));
        }
        finally
        {
            ToolCapabilityTestSeed.PublishBaseline();
        }
    }

    [Fact]
    public void Mcp_Qualified_Name_Resolves_When_Integration_Enabled()
    {
        try
        {
            var resolver = new ToolResolver([]);
            var config = EmptyConfig() with
            {
                Agentic = new AgenticConfig
                {
                    Tools = new AgenticToolsConfig
                    {
                        Integrations =
                        [
                            new IntegrationToolConfig
                            {
                                Type = "mcp",
                                Name = "zuora",
                                Enabled = true,
                                Url = "https://example.test/mcp"
                            }
                        ]
                    }
                }
            };

            Assert.True(resolver.TryResolve("zuora__query_objects", config, out var tool));
            Assert.True(tool.IsMcp);
            Assert.Equal(ToolHandlerKind.AppScoped, tool.Kind);
        }
        finally
        {
            ToolCapabilityTestSeed.PublishBaseline();
        }
    }

    [Fact]
    public void Mcp_Qualified_Name_Rejected_When_Integration_Disabled()
    {
        try
        {
            var resolver = new ToolResolver([]);
            var config = EmptyConfig() with
            {
                Agentic = new AgenticConfig
                {
                    Tools = new AgenticToolsConfig
                    {
                        Integrations =
                        [
                            new IntegrationToolConfig
                            {
                                Type = "mcp",
                                Name = "zuora",
                                Enabled = false,
                                Url = "https://example.test/mcp"
                            }
                        ]
                    }
                }
            };

            Assert.False(resolver.TryResolve("zuora__query_objects", config, out _));
        }
        finally
        {
            ToolCapabilityTestSeed.PublishBaseline();
        }
    }

    private static AppRuntimeConfig EmptyConfig() => new()
    {
        AppId = "test",
        Agentic = new AgenticConfig { Tools = new AgenticToolsConfig() }
    };

    private sealed class FakeDescriptor(params ToolRegistration[] regs) : IToolDescriptor
    {
        public IEnumerable<ToolRegistration> Describe() => regs;

        public IEnumerable<OllamaTool> BuildSchemas(AppRuntimeConfig runtimeConfig) =>
            regs.Select(r => new OllamaTool(
                "function",
                new OllamaFunction(r.WireName, "test", new { type = "object", properties = new { } })));
    }
}
