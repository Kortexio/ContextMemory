using System.Text.Json;
using ContextMemory.Api.Middleware;
using ContextMemory.Core.Agentic;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Engine;
using ContextMemory.Core.Models;
using ContextMemory.Core.WebSearch;

namespace ContextMemory.Api.Endpoints;

public static class ChatEndpoint
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static void MapChatEndpoint(this WebApplication app)
    {
        app.MapPost("/api/chat", HandleChatAsync).DisableAntiforgery();
    }

    private static async Task HandleChatAsync(
        HttpContext httpContext,
        OllamaRequest request,
        IContextEngine contextEngine,
        IAppConfigStore appConfigStore,
        ChatTurnContext chatTurnContext,
        CancellationToken cancellationToken)
    {
        var appId = (string)httpContext.Items[AuthMiddleware.AppIdItemKey]!;
        var userId = (string)httpContext.Items[AuthMiddleware.UserIdItemKey]!;
        var sessionId = (string)httpContext.Items[AuthMiddleware.SessionIdItemKey]!;

        chatTurnContext.Reset();

        var isStreaming = request.Stream ?? false;

        try
        {
            if (!isStreaming)
            {
                var result = await contextEngine
                    .ProcessChatAsync(appId, userId, sessionId, request, cancellationToken)
                    .ConfigureAwait(false);

                if (result.MessageId is not null)
                    httpContext.Response.Headers["X-Context-Memory-Message-Id"] = result.MessageId;

                ApplyWebSearchHeaders(httpContext, chatTurnContext.WebSearch);
                ApplyAgenticHeaders(httpContext, chatTurnContext.AgenticResult);

                httpContext.Response.StatusCode = StatusCodes.Status200OK;
                httpContext.Response.ContentType = "application/json";
                await httpContext.Response
                    .WriteAsJsonAsync(result.Response, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            httpContext.Response.StatusCode = StatusCodes.Status200OK;
            httpContext.Response.ContentType = "application/x-ndjson";

            var streamMessageId = contextEngine.ReserveStreamMessageId(appId, userId, request);
            if (streamMessageId is not null)
                httpContext.Response.Headers["X-Context-Memory-Message-Id"] = streamMessageId;

            await contextEngine
                .PrimeStreamTurnAsync(appId, userId, sessionId, request, cancellationToken)
                .ConfigureAwait(false);
            ApplyWebSearchHeaders(httpContext, chatTurnContext.WebSearch);
            ApplyAgenticStreamHeaders(httpContext, appConfigStore, appId);

            var assistantContent = new System.Text.StringBuilder();

            await foreach (var chunk in contextEngine
                .ProcessChatStreamAsync(appId, userId, sessionId, request, cancellationToken)
                .ConfigureAwait(false))
            {
                if (chunk.Message is not null)
                {
                    var content = OllamaLlmText.GetMessageContent(chunk.Message);
                    if (content.Length > 0)
                        assistantContent.Append(content);
                }

                var line = JsonSerializer.Serialize(chunk, JsonOptions);
                await httpContext.Response.WriteAsync(line + "\n", cancellationToken).ConfigureAwait(false);
                await httpContext.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            var final = await contextEngine
                .FinalizeStreamAsync(
                    appId, userId, sessionId, request, assistantContent.ToString(), streamMessageId, cancellationToken)
                .ConfigureAwait(false);

            if (final.MessageId is not null || chatTurnContext.AgenticResult is not null)
            {
                var trailer = new OllamaResponse
                {
                    Done = true,
                    ContextMemory = new ContextMemoryMetadata
                    {
                        MessageId = final.MessageId,
                        Agentic = chatTurnContext.AgenticResult is not null
                            ? AgenticStreamMetadata.FromResult(
                                chatTurnContext.AgenticResult,
                                appConfigStore.GetConfig(appId).DefaultLanguage)
                            : null
                    }
                };
                var trailerLine = JsonSerializer.Serialize(trailer, JsonOptions);
                await httpContext.Response.WriteAsync(trailerLine + "\n", cancellationToken).ConfigureAwait(false);
                await httpContext.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (HttpRequestException ex) when (ex.StatusCode.HasValue)
        {
            if (httpContext.Response.HasStarted)
                throw;

            httpContext.Response.StatusCode = (int)ex.StatusCode.Value;
            if (!string.IsNullOrEmpty(ex.Message))
            {
                httpContext.Response.ContentType = "application/json";
                await httpContext.Response.WriteAsync(ex.Message, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (IOException ex) when (!httpContext.Response.HasStarted)
        {
            httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            httpContext.Response.ContentType = "application/json";
            await httpContext.Response.WriteAsJsonAsync(new
            {
                error = "LLM backend unreachable or timed out.",
                detail = ex.Message
            }, cancellationToken).ConfigureAwait(false);
        }
    }

    private static void ApplyWebSearchHeaders(HttpContext httpContext, WebSearchEnrichment? enrichment)
    {
        if (enrichment is null)
            return;

        httpContext.Response.Headers["X-Web-Search-Used"] = enrichment.Used ? "true" : "false";
        if (!enrichment.Used && !string.IsNullOrWhiteSpace(enrichment.SkipReason))
            httpContext.Response.Headers["X-Web-Search-Skip-Reason"] = enrichment.SkipReason;
        if (enrichment.Used && !string.IsNullOrWhiteSpace(enrichment.Provider))
            httpContext.Response.Headers["X-Web-Search-Provider"] = enrichment.Provider;
    }

    private static void ApplyAgenticStreamHeaders(HttpContext httpContext, IAppConfigStore appConfigStore, string appId)
    {
        var runtimeConfig = appConfigStore.GetConfig(appId);
        if (!runtimeConfig.AgenticEnabled)
            return;

        httpContext.Response.Headers["X-Context-Memory-Agentic"] = "true";
        httpContext.Response.Headers["X-Context-Memory-Agentic-Progress"] = "true";
    }

    private static void ApplyAgenticHeaders(HttpContext httpContext, AgentResult? agenticResult)
    {
        if (agenticResult is null)
            return;

        httpContext.Response.Headers["X-Context-Memory-Agentic"] = "true";

        if (agenticResult.AwaitingConfirmation)
            httpContext.Response.Headers["X-Context-Memory-Agentic-Awaiting-Confirmation"] = "true";

        if (agenticResult.TimedOut)
            httpContext.Response.Headers["X-Context-Memory-Agentic-Timed-Out"] = "true";
    }
}
