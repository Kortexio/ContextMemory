namespace ContextMemory.Core.Api;

/// <summary>
/// Stable API version identifiers for ContextMemory public contracts.
/// </summary>
public static class ApiVersions
{
    public const string V1 = "v1";
    public const string V2 = "v2";
    public const string Current = V1;
    public const string HeaderName = "X-Context-Memory-Api-Version";
    public const string AcceptVersionHeader = "X-Api-Version";

    /// <summary>
    /// Resolves the requested API version from headers, falling back to <see cref="Current"/>.
    /// </summary>
    public static string Resolve(string? headerValue)
    {
        if (string.IsNullOrWhiteSpace(headerValue))
            return Current;

        var normalized = headerValue.Trim().TrimStart('v', 'V');
        return normalized switch
        {
            "1" => V1,
            "2" => V2,
            _ when string.Equals(headerValue.Trim(), V1, StringComparison.OrdinalIgnoreCase) => V1,
            _ when string.Equals(headerValue.Trim(), V2, StringComparison.OrdinalIgnoreCase) => V2,
            _ => Current
        };
    }

    public static bool IsSupported(string version) =>
        string.Equals(version, V1, StringComparison.OrdinalIgnoreCase)
        || string.Equals(version, V2, StringComparison.OrdinalIgnoreCase);
}
