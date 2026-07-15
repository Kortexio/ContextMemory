using System.Diagnostics;
using System.Text.Json;
using ContextMemory.Api.Middleware;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Exceptions;
using ContextMemory.Core.Models;
using ContextMemory.Core.Utilities;

namespace ContextMemory.Api.Endpoints;

public static class GenerateEndpoint
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static void MapGenerateEndpoint(this WebApplication app)
    {
        app.MapPost("/api/generate", HandleGenerateAsync).DisableAntiforgery();
    }

    private static async Task HandleGenerateAsync(
        HttpContext httpContext,
        OllamaGenerateRequest request,
        IAppRegistry appRegistry,
        IAppConfigStore appConfigStore,
        ILlmAdapterResolver adapterResolver,
        ITelemetryCollector telemetry,
        CancellationToken cancellationToken)
    {
        var appId = (string)httpContext.Items[AuthMiddleware.AppIdItemKey]!;
        var userId = (string)httpContext.Items[AuthMiddleware.UserIdItemKey]!;
        var sw = Stopwatch.StartNew();

        if (!appRegistry.TryGetApp(appId, out _))
            throw new AppNotFoundException(appId);

        var runtimeConfig = appConfigStore.GetConfig(appId);
        var adapter = adapterResolver.Resolve(runtimeConfig.LlmBackend);
        var isStreaming = request.Stream ?? false;

        try
        {
            if (!isStreaming)
            {
                var response = await adapter
                    .GenerateAsync(request, cancellationToken)
                    .ConfigureAwait(false);

                httpContext.Response.StatusCode = StatusCodes.Status200OK;
                httpContext.Response.ContentType = "application/json";
                await httpContext.Response
                    .WriteAsJsonAsync(response, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);

                RecordGenerateTelemetry(
                    telemetry, appId, userId, sw, request.Prompt, response.Response, StatusCodes.Status200OK);
                return;
            }

            httpContext.Response.StatusCode = StatusCodes.Status200OK;
            httpContext.Response.ContentType = "application/x-ndjson";

            var generated = new System.Text.StringBuilder();
            await foreach (var chunk in adapter
                .GenerateStreamAsync(request, cancellationToken)
                .ConfigureAwait(false))
            {
                if (chunk.Response is { Length: > 0 } part)
                    generated.Append(part);

                var line = JsonSerializer.Serialize(chunk, JsonOptions);
                await httpContext.Response.WriteAsync(line + "\n", cancellationToken).ConfigureAwait(false);
                await httpContext.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            RecordGenerateTelemetry(
                telemetry, appId, userId, sw, request.Prompt, generated.ToString(), StatusCodes.Status200OK);
        }
        catch (HttpRequestException ex) when (ex.StatusCode.HasValue)
        {
            RecordGenerateTelemetry(telemetry, appId, userId, sw, request.Prompt, null, (int)ex.StatusCode.Value);
            httpContext.Response.StatusCode = (int)ex.StatusCode.Value;
            if (!string.IsNullOrEmpty(ex.Message))
            {
                httpContext.Response.ContentType = "application/json";
                await httpContext.Response.WriteAsync(ex.Message, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static void RecordGenerateTelemetry(
        ITelemetryCollector telemetry,
        string appId,
        string userId,
        Stopwatch sw,
        string prompt,
        string? response,
        int statusCode)
    {
        sw.Stop();
        var promptTokens = TokenEstimator.Estimate(prompt);
        var completionTokens = TokenEstimator.Estimate(response);
        telemetry.RecordRequest(appId, userId, statusCode, sw.ElapsedMilliseconds, promptTokens, completionTokens);
    }
}
