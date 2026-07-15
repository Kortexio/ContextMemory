using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;

namespace ContextMemory.Api.Endpoints;

public static class AppsRegisterEndpoint
{
    public static void MapAppsRegisterEndpoint(this WebApplication app)
    {
        app.MapPost("/apps/register", RegisterAppAsync)
            .DisableAntiforgery();
    }

    private static async Task<IResult> RegisterAppAsync(
        RegisterAppRequest request,
        IAppRegistrationService registrationService,
        CancellationToken cancellationToken)
    {
        var response = await registrationService
            .RegisterAsync(request, cancellationToken)
            .ConfigureAwait(false);
        return Results.Json(response, statusCode: StatusCodes.Status201Created);
    }
}
