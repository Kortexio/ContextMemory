using System.Net;
using System.Net.Http;
using ContextMemory.Core.Agentic;
using Xunit;

namespace ContextMemory.Api.Tests;

public sealed class LlmContextLengthErrorTests
{
    private const string OllamaBody =
        """{"error":"{\"error\":{\"code\":400,\"message\":\"request (4302 tokens) exceeds the available context size (4096 tokens), try increasing it\",\"type\":\"exceed_context_size_error\",\"n_prompt_tokens\":4302,\"n_ctx\":4096}}"}""";

    [Fact]
    public void IsMatch_DetectsOllamaExceedContextSize()
    {
        var ex = new HttpRequestException(OllamaBody, null, HttpStatusCode.BadRequest);
        Assert.True(LlmContextLengthError.IsMatch(ex));
        Assert.False(LlmContextLengthError.IsMatch(new HttpRequestException("failed to parse grammar")));
    }

    [Fact]
    public void TryGet_ParsesPromptAndContextSize()
    {
        Assert.Equal(4096, LlmContextLengthError.TryGetContextSize(OllamaBody));
        Assert.Equal(4302, LlmContextLengthError.TryGetPromptTokens(OllamaBody));
    }
}
