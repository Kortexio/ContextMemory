using System.Net;

namespace ContextMemory.Adapters;

/// <summary>Cheap LLM-backend liveness — never list models.</summary>
internal static class LlmHost
{
    /// <summary>Origin without an OpenAI <c>/v1</c> suffix.</summary>
    public static string Origin(string endpoint)
    {
        var trimmed = endpoint.Trim().TrimEnd('/');
        if (trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            return trimmed[..^3].TrimEnd('/');
        return trimmed;
    }

    /// <summary>
    /// Any completed HTTP response below 500 means the process answered.
    /// 401/403/404 are reachable; connection failures are handled by the caller.
    /// </summary>
    public static bool IsReachable(HttpStatusCode status) => (int)status is >= 100 and < 500;
}
