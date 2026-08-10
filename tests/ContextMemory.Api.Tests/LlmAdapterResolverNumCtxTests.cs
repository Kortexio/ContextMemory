using ContextMemory.Adapters;
using ContextMemory.Core.Models;
using Xunit;

namespace ContextMemory.Api.Tests;

public sealed class LlmAdapterResolverNumCtxTests
{
    [Fact]
    public void ShouldPreferOllamaNative_WhenOllamaAndNumCtxSet()
    {
        var config = new AppRuntimeConfig
        {
            AppId = "test",
            LlmBackend = "ollama",
            LlmOptions = new LlmGenerationConfig { NumCtx = 32768 }
        };

        Assert.True(LlmAdapterResolver.ShouldPreferOllamaNativeForNumCtx(config));
    }

    [Fact]
    public void ShouldNotPreferNative_WhenAlreadyNativeOrNoNumCtx()
    {
        Assert.False(LlmAdapterResolver.ShouldPreferOllamaNativeForNumCtx(new AppRuntimeConfig
        {
            AppId = "t",
            LlmBackend = "ollama-native",
            LlmOptions = new LlmGenerationConfig { NumCtx = 32768 }
        }));

        Assert.False(LlmAdapterResolver.ShouldPreferOllamaNativeForNumCtx(new AppRuntimeConfig
        {
            AppId = "t",
            LlmBackend = "ollama",
            LlmOptions = new LlmGenerationConfig()
        }));

        Assert.False(LlmAdapterResolver.ShouldPreferOllamaNativeForNumCtx(new AppRuntimeConfig
        {
            AppId = "t",
            LlmBackend = "openai",
            LlmOptions = new LlmGenerationConfig { NumCtx = 8192 }
        }));
    }
}
