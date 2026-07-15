using ContextMemory.Api.Middleware;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;

namespace ContextMemory.Api.Endpoints;

public static class AppsConfigEndpoint
{
    public static void MapAppsConfigEndpoints(this WebApplication app)
    {
        app.MapGet("/apps/{appId}/config", GetConfigAsync);
        app.MapPatch("/apps/{appId}/config", PatchConfigAsync);
    }

    private static IResult GetConfigAsync(
        HttpContext httpContext,
        string appId,
        IAppConfigStore appConfigStore)
    {
        if (!ValidateAppAccess(httpContext, appId, out var error))
            return error!;

        return Results.Json(appConfigStore.GetConfig(appId));
    }

    private static async Task<IResult> PatchConfigAsync(
        HttpContext httpContext,
        string appId,
        AppConfigPatchRequest patch,
        IAppConfigStore appConfigStore,
        CancellationToken cancellationToken)
    {
        if (!ValidateAppAccess(httpContext, appId, out var error))
            return error!;

        var updated = await appConfigStore
            .UpdateAsync(appId, patch, cancellationToken)
            .ConfigureAwait(false);

        return Results.Json(updated);
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

