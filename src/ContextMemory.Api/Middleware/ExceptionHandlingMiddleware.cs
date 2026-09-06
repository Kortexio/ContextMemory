using ContextMemory.Core.Exceptions;

namespace ContextMemory.Api.Middleware;

public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger,
        IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context).ConfigureAwait(false);
        }
        catch (Exception ex) when (!context.Response.HasStarted)
        {
            await HandleAsync(context, ex).ConfigureAwait(false);
        }
    }

    private async Task HandleAsync(HttpContext context, Exception exception)
    {
        var (statusCode, message, logLevel) = MapException(exception);

        if (logLevel == LogLevel.Error)
            _logger.LogError(exception, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
        else
            _logger.LogDebug(exception, "Request rejected: {Message}", message);

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";

        object body;
        if (exception is ContextMemoryErrorException cmEx)
        {
            var error = cmEx.Error;
            if (string.IsNullOrWhiteSpace(error.TraceId))
            {
                error = ContextMemoryError.Create(
                    error.Code,
                    error.Message,
                    error.Detail,
                    context.TraceIdentifier,
                    error.Metadata);
            }

            body = new { error };
        }
        else if (statusCode >= StatusCodes.Status500InternalServerError && !_environment.IsDevelopment())
        {
            body = new
            {
                error = ContextMemoryError.Create(
                    ContextMemoryErrorCodes.Unknown,
                    "An unexpected error occurred.",
                    traceId: context.TraceIdentifier)
            };
        }
        else
        {
            body = new
            {
                error = ContextMemoryError.Create(
                    MapErrorCode(exception),
                    message,
                    traceId: context.TraceIdentifier)
            };
        }

        await context.Response.WriteAsJsonAsync(body).ConfigureAwait(false);
    }

    private static (int StatusCode, string Message, LogLevel LogLevel) MapException(Exception exception) =>
        exception switch
        {
            ContextMemoryErrorException ex => (ex.StatusCode, ex.Message, LogLevel.Debug),
            AppNotFoundException ex => (StatusCodes.Status404NotFound, ex.Message, LogLevel.Debug),
            AppAlreadyExistsException ex => (StatusCodes.Status409Conflict, ex.Message, LogLevel.Debug),
            ArgumentException ex => (StatusCodes.Status400BadRequest, ex.Message, LogLevel.Debug),
            _ => (StatusCodes.Status500InternalServerError, exception.Message, LogLevel.Error)
        };

    private static string MapErrorCode(Exception exception) =>
        exception switch
        {
            AppNotFoundException => ContextMemoryErrorCodes.NotFound,
            AppAlreadyExistsException => ContextMemoryErrorCodes.Conflict,
            ArgumentException => ContextMemoryErrorCodes.Validation,
            TimeoutException => ContextMemoryErrorCodes.Timeout,
            _ => ContextMemoryErrorCodes.Unknown
        };
}
