using ContextMemory.Core.Contracts;
using ContextMemory.Core.Security;

namespace ContextMemory.Api.Endpoints;

public static class AdminSessionsEndpoint
{
    public static void MapAdminSessionsEndpoints(this WebApplication app)
    {
        app.MapDelete("/admin/sessions", DeleteSessionsOlderThan);
        app.MapDelete("/admin/sessions/{appId}/{userId}", DeleteSessionsForUser);
    }

    private static async Task<IResult> DeleteSessionsOlderThan(
        string appId,
        string olderThan,
        ISessionStore sessionStore,
        CancellationToken cancellationToken)
    {
        if (!IdentifierValidator.IsValid(appId))
            return Results.BadRequest(new { error = "Invalid appId format." });

        if (!DateTimeOffset.TryParse(olderThan, out var cutoff))
            return Results.BadRequest(new { error = "Invalid olderThan — use ISO8601 format." });

        var deletedCount = await sessionStore
            .DeleteSessionsOlderThanAsync(appId, cutoff, cancellationToken)
            .ConfigureAwait(false);

        return Results.Json(new { deletedCount });
    }

    private static async Task<IResult> DeleteSessionsForUser(
        string appId,
        string userId,
        ISessionStore sessionStore,
        CancellationToken cancellationToken)
    {
        if (!IdentifierValidator.IsValid(appId) || !IdentifierValidator.IsValid(userId))
            return Results.BadRequest(new { error = "Invalid appId or userId format." });

        await sessionStore.DeleteSessionsForUserAsync(appId, userId, cancellationToken).ConfigureAwait(false);
        return Results.NoContent();
    }
}
