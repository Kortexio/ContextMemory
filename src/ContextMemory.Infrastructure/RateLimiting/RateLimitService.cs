using System.Collections.Concurrent;
using System.Threading.RateLimiting;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;

namespace ContextMemory.Infrastructure.RateLimiting;

public sealed class RateLimitService : IRateLimitService, IDisposable
{
    private readonly ConcurrentDictionary<string, RateLimiter> _appRequestLimiters = new();
    private readonly ConcurrentDictionary<string, RateLimiter> _userRequestLimiters = new();
    private readonly ConcurrentDictionary<string, TokenBucket> _appTokenBuckets = new();

    public RateLimitAcquireResult TryAcquire(
        string appId,
        string userId,
        int estimatedTokens,
        RateLimitConfig config)
    {
        var appRequestLimiter = _appRequestLimiters.GetOrAdd(
            appId,
            _ => new SlidingWindowRateLimiter(new SlidingWindowRateLimiterOptions
            {
                PermitLimit = Math.Max(1, config.RequestsPerMinute),
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 6,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

        if (!appRequestLimiter.AttemptAcquire(1).IsAcquired)
            return RateLimitAcquireResult.Rejected(60);

        var userKey = $"{appId}:{userId}";
        var userRequestLimiter = _userRequestLimiters.GetOrAdd(
            userKey,
            _ => new SlidingWindowRateLimiter(new SlidingWindowRateLimiterOptions
            {
                PermitLimit = Math.Max(1, config.UserRequestsPerMinute),
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 6,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

        if (!userRequestLimiter.AttemptAcquire(1).IsAcquired)
            return RateLimitAcquireResult.Rejected(60);

        var tokenBucket = _appTokenBuckets.GetOrAdd(appId, _ => new TokenBucket(config.TokensPerMinute));
        if (!tokenBucket.TryConsume(estimatedTokens))
            return RateLimitAcquireResult.Rejected(60);

        return RateLimitAcquireResult.Success();
    }

    public void ChargeAdditional(string appId, int additionalTokens, int additionalRequests = 0)
    {
        if (additionalTokens > 0 && _appTokenBuckets.TryGetValue(appId, out var tokenBucket))
            tokenBucket.ForceConsume(additionalTokens);

        if (additionalRequests <= 0)
            return;

        if (_appRequestLimiters.TryGetValue(appId, out var appLimiter))
        {
            for (var i = 0; i < additionalRequests; i++)
                appLimiter.AttemptAcquire(1);
        }
    }

    public void Dispose()
    {
        foreach (var limiter in _appRequestLimiters.Values)
            limiter.Dispose();
        foreach (var limiter in _userRequestLimiters.Values)
            limiter.Dispose();
    }

    private sealed class TokenBucket
    {
        private readonly int _maxTokensPerMinute;
        private readonly object _lock = new();
        private double _tokens;
        private DateTimeOffset _lastRefill = DateTimeOffset.UtcNow;

        public TokenBucket(int maxTokensPerMinute)
        {
            _maxTokensPerMinute = Math.Max(1000, maxTokensPerMinute);
            _tokens = _maxTokensPerMinute;
        }

        public bool TryConsume(int amount)
        {
            lock (_lock)
            {
                Refill();
                if (_tokens < amount)
                    return false;

                _tokens -= amount;
                return true;
            }
        }

        public void ForceConsume(int amount)
        {
            lock (_lock)
            {
                Refill();
                _tokens = Math.Max(0, _tokens - Math.Max(0, amount));
            }
        }

        private void Refill()
        {
            var now = DateTimeOffset.UtcNow;
            var elapsed = now - _lastRefill;
            if (elapsed < TimeSpan.FromMilliseconds(100))
                return;

            var tokensToAdd = _maxTokensPerMinute * elapsed.TotalMinutes;
            _tokens = Math.Min(_maxTokensPerMinute, _tokens + tokensToAdd);
            _lastRefill = now;
        }
    }
}
