using System.Diagnostics;

namespace ContextMemory.Api.Middleware;

public sealed class TelemetryMiddleware
{
    private readonly RequestDelegate _next;

    public TelemetryMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var sw = Stopwatch.StartNew();
        await _next(context).ConfigureAwait(false);
        sw.Stop();

        if (!context.Response.HasStarted)
            context.Response.Headers["X-Response-Time-Ms"] = sw.ElapsedMilliseconds.ToString();
    }
}
