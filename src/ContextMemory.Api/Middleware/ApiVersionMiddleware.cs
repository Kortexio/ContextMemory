using ContextMemory.Core.Api;
using ContextMemory.Core.Exceptions;

namespace ContextMemory.Api.Middleware;

/// <summary>
/// Resolves and echoes API version headers (CM-1).
/// </summary>
public sealed class ApiVersionMiddleware
{
    private readonly RequestDelegate _next;

    public ApiVersionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var requested = context.Request.Headers[ApiVersions.AcceptVersionHeader].FirstOrDefault()
            ?? context.Request.Headers[ApiVersions.HeaderName].FirstOrDefault();

        var version = ApiVersions.Resolve(requested);
        if (!ApiVersions.IsSupported(version))
            throw ContextMemoryErrorException.UnsupportedApiVersion(requested ?? version);

        context.Items[ApiVersions.HeaderName] = version;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[ApiVersions.HeaderName] = version;
            return Task.CompletedTask;
        });

        await _next(context).ConfigureAwait(false);
    }
}
