using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Security;
using Microsoft.Extensions.Options;

namespace ContextMemory.Api.Middleware;

public sealed class AuthMiddleware
{
    public const string AppIdItemKey = "ContextMemory.AppId";
    public const string UserIdItemKey = "ContextMemory.UserId";
    public const string SessionIdItemKey = "ContextMemory.SessionId";

    private const string AppIdHeader = "X-App-Id";
    private const string UserIdHeader = "X-User-Id";
    private const string SessionIdHeader = "X-Session-Id";

    private readonly RequestDelegate _next;
    private readonly IAppRegistry _appRegistry;
    private readonly string _masterKey;
    private readonly int _maxPayloadBytes;

    public AuthMiddleware(
        RequestDelegate next,
        IAppRegistry appRegistry,
        IOptions<ContextMemoryOptions> options)
    {
        _next = next;
        _appRegistry = appRegistry;
        var config = options.Value;
        _masterKey = config.MasterKey;
        _maxPayloadBytes = config.MaxPayloadBytes;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;

        if (IsPublicPath(path))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        if (RequiresMasterKey(path))
        {
            if (!ValidateMasterKey(context))
            {
                await WriteJsonErrorAsync(context, StatusCodes.Status401Unauthorized, "Invalid master key.")
                    .ConfigureAwait(false);
                return;
            }

            await _next(context).ConfigureAwait(false);
            return;
        }

        if (!RequiresAuth(path))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        if (context.Request.ContentLength is > 0 and var length && length > _maxPayloadBytes)
        {
            await WriteJsonErrorAsync(context, StatusCodes.Status413PayloadTooLarge, "Payload too large.")
                .ConfigureAwait(false);
            return;
        }

        if (!context.Request.Headers.TryGetValue(AppIdHeader, out var appIdValues)
            || !context.Request.Headers.TryGetValue(UserIdHeader, out var userIdValues))
        {
            await WriteJsonErrorAsync(context, StatusCodes.Status401Unauthorized, "Missing X-App-Id or X-User-Id header.")
                .ConfigureAwait(false);
            return;
        }

        var appId = appIdValues.ToString();
        var userId = userIdValues.ToString();

        if (!IdentifierValidator.IsValid(appId) || !IdentifierValidator.IsValid(userId))
        {
            await WriteJsonErrorAsync(context, StatusCodes.Status400BadRequest, "Invalid appId or userId format.")
                .ConfigureAwait(false);
            return;
        }

        if (!TryGetBearerToken(context, out var apiKey))
        {
            await WriteJsonErrorAsync(context, StatusCodes.Status401Unauthorized, "Missing or invalid Authorization header.")
                .ConfigureAwait(false);
            return;
        }

        if (!_appRegistry.TryGetApp(appId, out var appProfile) || appProfile is null
            || !_appRegistry.ValidateApiKey(appId, apiKey))
        {
            await WriteJsonErrorAsync(context, StatusCodes.Status401Unauthorized, "Invalid appId or API key.")
                .ConfigureAwait(false);
            return;
        }

        if (!appProfile.IsActive)
        {
            await WriteJsonErrorAsync(context, StatusCodes.Status403Forbidden, "App is inactive.")
                .ConfigureAwait(false);
            return;
        }

        context.Items[AppIdItemKey] = appId;
        context.Items[UserIdItemKey] = userId;

        var sessionId = context.Request.Headers.TryGetValue(SessionIdHeader, out var sessionValues)
                        && IdentifierValidator.IsValid(sessionValues.ToString())
            ? sessionValues.ToString()!
            : Guid.NewGuid().ToString("N");

        context.Items[SessionIdItemKey] = sessionId;
        context.Response.Headers["X-Session-Id"] = sessionId;

        await _next(context).ConfigureAwait(false);
    }

    private bool ValidateMasterKey(HttpContext context)
    {
        if (string.IsNullOrWhiteSpace(_masterKey))
            return false;

        return TryGetBearerToken(context, out var token)
            && string.Equals(token, _masterKey, StringComparison.Ordinal);
    }

    private static Task WriteJsonErrorAsync(HttpContext context, int statusCode, string error)
    {
        context.Response.StatusCode = statusCode;
        return context.Response.WriteAsJsonAsync(new { error });
    }

    private static bool IsPublicPath(PathString path) =>
        path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/metrics", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/swagger", StringComparison.OrdinalIgnoreCase)
        // HTML landing only — /admin/apps and other admin APIs still require the master key.
        || path.Equals("/admin", StringComparison.OrdinalIgnoreCase);

    private static bool RequiresMasterKey(PathString path) =>
        path.StartsWithSegments("/admin", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/apps/register", StringComparison.OrdinalIgnoreCase);

    private static bool RequiresAuth(PathString path) =>
        path.StartsWithSegments("/api/chat", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/api/generate", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/apps", StringComparison.OrdinalIgnoreCase);

    private static bool TryGetBearerToken(HttpContext context, out string token)
    {
        token = string.Empty;
        var header = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header)
            || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return false;

        token = header["Bearer ".Length..].Trim();
        return token.Length > 0;
    }
}
