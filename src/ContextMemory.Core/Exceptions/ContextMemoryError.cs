namespace ContextMemory.Core.Exceptions;

/// <summary>
/// Standardized error codes for ContextMemory API and runtime failures.
/// </summary>
public static class ContextMemoryErrorCodes
{
    public const string Unknown = "cm.unknown";
    public const string Validation = "cm.validation";
    public const string NotFound = "cm.not_found";
    public const string Conflict = "cm.conflict";
    public const string Unauthorized = "cm.unauthorized";
    public const string Forbidden = "cm.forbidden";
    public const string RateLimited = "cm.rate_limited";
    public const string Timeout = "cm.timeout";
    public const string LlmBackend = "cm.llm_backend";
    public const string AgenticLoop = "cm.agentic_loop";
    public const string ToolExecution = "cm.tool_execution";
    public const string PolicyDenied = "cm.policy_denied";
    public const string SessionState = "cm.session_state";
    public const string UnsupportedApiVersion = "cm.unsupported_api_version";
}

/// <summary>
/// Structured error payload returned to API clients.
/// </summary>
public sealed class ContextMemoryError
{
    public required string Code { get; init; }
    public required string Message { get; init; }
    public string? Detail { get; init; }
    public string? TraceId { get; init; }
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }

    public static ContextMemoryError Create(
        string code,
        string message,
        string? detail = null,
        string? traceId = null,
        IReadOnlyDictionary<string, string>? metadata = null) =>
        new()
        {
            Code = code,
            Message = message,
            Detail = detail,
            TraceId = traceId,
            Metadata = metadata
        };
}

/// <summary>
/// Exception carrying a <see cref="ContextMemoryError"/> for middleware mapping.
/// </summary>
public sealed class ContextMemoryErrorException : ContextMemoryException
{
    public ContextMemoryError Error { get; }
    public int StatusCode { get; }

    public ContextMemoryErrorException(ContextMemoryError error, int statusCode = 400)
        : base(error.Message)
    {
        Error = error;
        StatusCode = statusCode;
    }

    public static ContextMemoryErrorException Validation(string message, string? detail = null) =>
        new(ContextMemoryError.Create(ContextMemoryErrorCodes.Validation, message, detail), 400);

    public static ContextMemoryErrorException NotFound(string message) =>
        new(ContextMemoryError.Create(ContextMemoryErrorCodes.NotFound, message), 404);

    public static ContextMemoryErrorException Conflict(string message) =>
        new(ContextMemoryError.Create(ContextMemoryErrorCodes.Conflict, message), 409);

    public static ContextMemoryErrorException PolicyDenied(string message) =>
        new(ContextMemoryError.Create(ContextMemoryErrorCodes.PolicyDenied, message), 403);

    public static ContextMemoryErrorException UnsupportedApiVersion(string version) =>
        new(
            ContextMemoryError.Create(
                ContextMemoryErrorCodes.UnsupportedApiVersion,
                $"Unsupported API version '{version}'.",
                detail: $"Supported: {Api.ApiVersions.V1}, {Api.ApiVersions.V2}"),
            400);
}
