using ContextMemory.Api.Middleware;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.GlobalWiki;
using ContextMemory.Core.Models;

namespace ContextMemory.Api.Endpoints;

public static class GlobalWikiEndpoint
{
    public static void MapGlobalWikiEndpoints(this WebApplication app)
    {
        app.MapPut("/apps/{appId}/wiki/documents/{documentId}", UpsertAsync);
        app.MapPost("/apps/{appId}/wiki/documents/batch", UpsertBatchAsync);
        app.MapDelete("/apps/{appId}/wiki/documents/{documentId}", DeleteAsync);
        app.MapGet("/apps/{appId}/wiki/documents", ListAsync);
        app.MapPost("/apps/{appId}/wiki/query", QueryAsync);
    }

    private static async Task<IResult> UpsertAsync(
        HttpContext httpContext,
        string appId,
        string documentId,
        GlobalWikiUpsertRequest request,
        GlobalWikiService wikiService,
        CancellationToken cancellationToken)
    {
        if (!ValidateAppAccess(httpContext, appId, out var error))
            return error!;

        documentId = Uri.UnescapeDataString(documentId);
        if (string.IsNullOrWhiteSpace(documentId) || string.IsNullOrWhiteSpace(request.Content))
        {
            return Results.Json(
                new { error = "documentId and content are required." },
                statusCode: StatusCodes.Status400BadRequest);
        }

        var result = await wikiService
            .UpsertAsync(appId, documentId, request, cancellationToken)
            .ConfigureAwait(false);

        return result.Created
            ? Results.Json(result, statusCode: StatusCodes.Status201Created)
            : Results.Json(result);
    }

    private static async Task<IResult> UpsertBatchAsync(
        HttpContext httpContext,
        string appId,
        GlobalWikiBatchUpsertRequest request,
        GlobalWikiService wikiService,
        CancellationToken cancellationToken)
    {
        if (!ValidateAppAccess(httpContext, appId, out var error))
            return error!;

        var result = await wikiService
            .UpsertBatchAsync(appId, request, cancellationToken)
            .ConfigureAwait(false);
        return Results.Json(result);
    }

    private static async Task<IResult> DeleteAsync(
        HttpContext httpContext,
        string appId,
        string documentId,
        GlobalWikiService wikiService,
        CancellationToken cancellationToken)
    {
        if (!ValidateAppAccess(httpContext, appId, out var error))
            return error!;

        documentId = Uri.UnescapeDataString(documentId);
        var deleted = await wikiService.DeleteAsync(appId, documentId, cancellationToken).ConfigureAwait(false);
        return deleted ? Results.NoContent() : Results.NotFound(new { error = "Document not found." });
    }

    private static async Task<IResult> ListAsync(
        HttpContext httpContext,
        string appId,
        GlobalWikiService wikiService,
        CancellationToken cancellationToken,
        string? sourceId = null,
        int offset = 0,
        int limit = 50)
    {
        if (!ValidateAppAccess(httpContext, appId, out var error))
            return error!;

        var result = await wikiService
            .ListAsync(appId, sourceId, offset, limit, cancellationToken)
            .ConfigureAwait(false);
        return Results.Json(result);
    }

    private static async Task<IResult> QueryAsync(
        HttpContext httpContext,
        string appId,
        GlobalWikiQueryRequest request,
        GlobalWikiService wikiService,
        IAppConfigStore appConfigStore,
        CancellationToken cancellationToken)
    {
        if (!ValidateAppAccess(httpContext, appId, out var error))
            return error!;

        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return Results.Json(
                new { error = "query is required." },
                statusCode: StatusCodes.Status400BadRequest);
        }

        var config = appConfigStore.GetConfig(appId);
        var budget = request.BudgetChars > 0
            ? request.BudgetChars
            : config.MaxGlobalWikiToolChars > 0
                ? config.MaxGlobalWikiToolChars
                : GlobalWikiService.DefaultBudgetChars;

        var result = await wikiService
            .QueryAsync(appId, request, budget, cancellationToken)
            .ConfigureAwait(false);
        return Results.Json(result);
    }

    private static bool ValidateAppAccess(HttpContext httpContext, string appId, out IResult? error)
    {
        error = null;
        var headerAppId = httpContext.Items[AuthMiddleware.AppIdItemKey] as string;

        if (!string.Equals(headerAppId, appId, StringComparison.Ordinal))
        {
            error = Results.Json(
                new { error = "X-App-Id does not match the requested appId." },
                statusCode: StatusCodes.Status403Forbidden);
            return false;
        }

        return true;
    }
}
