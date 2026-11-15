using ContextMemory.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ContextMemory.Api.Tests.Harness;

public sealed class DiSmokeTests : IClassFixture<AgenticStubWebApplicationFactory>
{
    private readonly AgenticStubWebApplicationFactory _factory;

    public DiSmokeTests(AgenticStubWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public void Resolves_ToolRegistry_And_Processor()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;

        var registry = sp.GetRequiredService<IToolRegistry>();
        Assert.NotEmpty(registry.Descriptors);

        var resolver = sp.GetRequiredService<IToolResolver>();
        Assert.NotNull(resolver);

        var processor = sp.GetRequiredService<IAgentToolCallProcessor>();
        Assert.NotNull(processor);

        var tools = sp.GetRequiredService<IAgenticToolRegistry>();
        Assert.NotNull(tools);
    }
}
