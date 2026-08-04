using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;

namespace ContextMemory.Api.Endpoints;

public static class AdminPlatformDefaultsEndpoint
{
    public static void MapAdminPlatformDefaultsEndpoints(this WebApplication app)
    {
        app.MapGet("/admin/platform-defaults", GetPlatformDefaults);
        app.MapPatch("/admin/platform-defaults", PatchPlatformDefaults);
    }

    private static IResult GetPlatformDefaults(IPlatformDefaultsStore store) =>
        Results.Json(store.Get());

    private static async Task<IResult> PatchPlatformDefaults(
        PlatformDefaultsPatchRequest patch,
        IPlatformDefaultsStore store,
        CancellationToken cancellationToken)
    {
        var updated = await store.UpdateAsync(patch, cancellationToken).ConfigureAwait(false);
        return Results.Json(updated);
    }
}
