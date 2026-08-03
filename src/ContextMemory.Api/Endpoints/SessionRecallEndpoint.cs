using ContextMemory.Api.Middleware;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Session;

namespace ContextMemory.Api.Endpoints;

public static class SessionRecallEndpoint
{
    public static void MapSessionRecallEndpoints(this WebApplication app)
    {
        app.MapGet("/apps/{appId}/sessions/{userId}/{sessionId}/wiki", RecallAsync);
    }

    private static async Task<IResult> RecallAsync(
        HttpContext httpContext,
        string appId,
        string userId,
        string sessionId,
        ISessionStore sessionStore,
        IAppConfigStore appConfigStore,
        CancellationToken cancellationToken,
        string? query = null,
        int budgetChars = 8_000)
    {
        var headerAppId = httpContext.Items[AuthMiddleware.AppIdItemKey] as string;
        if (!string.Equals(headerAppId, appId, StringComparison.Ordinal))
        {
            return Results.Json(
                new { error = "X-App-Id does not match the requested appId." },
                statusCode: StatusCodes.Status403Forbidden);
        }

        userId = Uri.UnescapeDataString(userId);
        sessionId = Uri.UnescapeDataString(sessionId);
        var config = appConfigStore.GetConfig(appId);
        var budget = budgetChars > 0
            ? budgetChars
            : config.MaxWikiContextChars > 0 ? config.MaxWikiContextChars : 8_000;

        var snapshot = await sessionStore
            .LoadAsync(appId, userId, sessionId, cancellationToken)
            .ConfigureAwait(false);

        var compiled = SessionWikiCompiler.Compile(snapshot, query, budget, includeIndex: true);
        return Results.Json(new
        {
            appId,
            userId,
            sessionId,
            compiledMarkdown = compiled.Content,
            charCount = compiled.CharCount,
            includedPages = compiled.IncludedPages,
            totalPages = compiled.TotalPages,
            truncated = compiled.Truncated
        });
    }
}
