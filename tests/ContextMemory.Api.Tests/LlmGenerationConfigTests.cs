using ContextMemory.Core.Models;
using Xunit;

namespace ContextMemory.Api.Tests;

public sealed class LlmGenerationConfigTests
{
    [Fact]
    public void MergeOptions_fills_tenant_defaults_when_request_null()
    {
        var tenant = new LlmGenerationConfig { Temperature = 0.2f, NumCtx = 32768, NumPredict = 4096 };
        var merged = LlmGenerationConfig.MergeOptions(tenant, null);
        Assert.NotNull(merged);
        Assert.Equal(0.2f, merged!.Temperature);
        Assert.Equal(32768, merged.NumCtx);
        Assert.Equal(4096, merged.NumPredict);
    }

    [Fact]
    public void MergeOptions_request_overrides_tenant()
    {
        var tenant = new LlmGenerationConfig { Temperature = 0.2f, NumCtx = 8192, TopP = 0.9f };
        var request = new OllamaOptions { Temperature = 0.7f, NumPredict = 512 };
        var merged = LlmGenerationConfig.MergeOptions(tenant, request)!;
        Assert.Equal(0.7f, merged.Temperature);
        Assert.Equal(8192, merged.NumCtx);
        Assert.Equal(0.9f, merged.TopP);
        Assert.Equal(512, merged.NumPredict);
    }

    [Fact]
    public void MergeKeepAlive_and_Format_prefer_request()
    {
        var tenant = new LlmGenerationConfig { KeepAlive = "5m", Format = "json" };
        Assert.Equal("10m", LlmGenerationConfig.MergeKeepAlive(tenant, "10m"));
        Assert.Equal("5m", LlmGenerationConfig.MergeKeepAlive(tenant, null));
        Assert.Equal("json", LlmGenerationConfig.MergeFormat(tenant, "  "));
        Assert.Equal("text", LlmGenerationConfig.MergeFormat(tenant, "text"));
    }
}
