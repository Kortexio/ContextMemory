using ContextMemory.Adapters.OpenAi;
using ContextMemory.Core.Agentic;
using ContextMemory.Core.Agentic.Prompts;
using ContextMemory.Core.Models;
using Xunit;

namespace ContextMemory.Api.Tests;

public sealed class HostAllowlistTests
{
    [Theory]
    [InlineData("https://example.com/a", "example.com", true)]
    [InlineData("https://api.example.com/a", "example.com", true)]
    [InlineData("https://evil.com/a", "example.com", false)]
    [InlineData("http://127.0.0.1/", "127.0.0.1", false)]
    [InlineData("file:///etc/passwd", "etc", false)]
    public void TryValidatePublicHttpUrl_RespectsAllowlistAndSsrf(string url, string allowed, bool ok)
    {
        var hosts = new List<string> { allowed };
        var result = HostAllowlist.TryValidatePublicHttpUrl(url, hosts, out _, out var error);
        Assert.Equal(ok, result);
        if (!ok)
            Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void EmptyAllowlist_IsFailClosed()
    {
        Assert.False(HostAllowlist.TryValidatePublicHttpUrl(
            "https://example.com", [], out _, out _));
    }
}

public sealed class AgenticGatewayToolsRegistryTests
{
    [Fact]
    public void HttpTools_AppearOnlyWhenEnabled()
    {
        var off = new AppRuntimeConfig { AppId = "t", Agentic = new AgenticConfig { Tools = new AgenticToolsConfig() } };
        Assert.Empty(AgenticHttpTools.BuildTools(off));

        var on = off with
        {
            Agentic = new AgenticConfig
            {
                Tools = new AgenticToolsConfig
                {
                    Http = new AgenticHttpToolsConfig { Enabled = true, AllowHttpRequest = true, AllowWebSearchTool = true }
                }
            }
        };
        var names = AgenticHttpTools.BuildTools(on).Select(t => t.Function.Name).ToHashSet();
        Assert.Contains(AgenticHttpTools.FetchUrl, names);
        Assert.Contains(AgenticHttpTools.HttpRequest, names);
        Assert.Contains(AgenticHttpTools.WebSearch, names);
    }

    [Fact]
    public void VisionTools_RequireSupportsVision()
    {
        var cfg = new AppRuntimeConfig
        {
            AppId = "t",
            LlmModel = "qwen3.5:9b",
            Agentic = new AgenticConfig
            {
                Tools = new AgenticToolsConfig { Vision = new AgenticVisionToolsConfig { Enabled = true } }
            }
        };
        Assert.Empty(AgenticVisionTools.BuildTools(cfg, supportsVision: false));
        Assert.NotEmpty(AgenticVisionTools.BuildTools(cfg, supportsVision: true));
    }

    [Theory]
    [InlineData("llava:13b", true)]
    [InlineData("gpt-4o", true)]
    [InlineData("qwen2.5-vl:7b", true)]
    [InlineData("qwen3.5:9b", false)]
    [InlineData("gemma4:12b", false)]
    public void LooksLikeVisionModel(string model, bool expected) =>
        Assert.Equal(expected, LlmCapabilitiesResolver.LooksLikeVisionModel(model));

    [Fact]
    public void HasAnyTools_IncludesHttp()
    {
        var cfg = new AgenticConfig
        {
            Tools = new AgenticToolsConfig { Http = new AgenticHttpToolsConfig { Enabled = true } }
        };
        Assert.True(cfg.HasAnyTools);
        Assert.True(cfg.HasHttpTools);
    }
}

public sealed class OpenAiMultimodalImageExtractionTests
{
    [Fact]
    public void ExtractImages_FromDataUrlAndRemote()
    {
        var content = System.Text.Json.JsonSerializer.SerializeToElement(new object[]
        {
            new { type = "text", text = "what is this?" },
            new { type = "image_url", image_url = new { url = "data:image/png;base64,QUJD" } },
            new { type = "image_url", image_url = new { url = "https://example.com/a.png" } }
        });

        var images = OpenAiProtocolMapper.ExtractImages(content);
        Assert.NotNull(images);
        Assert.Equal(2, images!.Count);
        Assert.Equal("QUJD", images[0]);
        Assert.Equal("https://example.com/a.png", images[1]);
    }
}
