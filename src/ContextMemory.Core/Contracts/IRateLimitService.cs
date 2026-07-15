using ContextMemory.Core.Models;

namespace ContextMemory.Core.Contracts;

/// <summary>
/// Enforces per-app and per-user request/token rate limits.
/// </summary>
public interface IRateLimitService
{
    RateLimitAcquireResult TryAcquire(string appId, string userId, int estimatedTokens, RateLimitConfig config);

    void ChargeAdditional(string appId, int additionalTokens, int additionalRequests = 0);
}

public sealed class RateLimitAcquireResult
{
    public bool IsAcquired { get; init; }
    public int RetryAfterSeconds { get; init; }

    public static RateLimitAcquireResult Success() => new() { IsAcquired = true };
    public static RateLimitAcquireResult Rejected(int retryAfterSeconds) =>
        new() { IsAcquired = false, RetryAfterSeconds = retryAfterSeconds };
}
