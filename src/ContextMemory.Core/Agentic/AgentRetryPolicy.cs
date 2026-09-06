namespace ContextMemory.Core.Agentic;

/// <summary>
/// Classification of failures for agent retry decisions (CM-4).
/// </summary>
public enum RetryClassification
{
    Transient = 0,
    Permanent = 1
}

/// <summary>
/// Retries transient LLM/network failures with exponential backoff.
/// </summary>
public sealed class AgentRetryPolicy
{
    public int MaxAttempts { get; init; } = 3;

    public int BaseDelayMs { get; init; } = 250;

    public RetryClassification Classify(Exception exception, CancellationToken cancellationToken = default)
    {
        if (exception is AggregateException aggregate)
        {
            var inner = aggregate.Flatten().InnerExceptions.FirstOrDefault() ?? aggregate;
            return Classify(inner, cancellationToken);
        }

        if (cancellationToken.IsCancellationRequested
            && exception is OperationCanceledException)
        {
            return RetryClassification.Permanent;
        }

        return Unwrap(exception) switch
        {
            HttpRequestException => RetryClassification.Transient,
            TimeoutException => RetryClassification.Transient,
            TaskCanceledException when !cancellationToken.IsCancellationRequested => RetryClassification.Transient,
            IOException => RetryClassification.Transient,
            OperationCanceledException when !cancellationToken.IsCancellationRequested => RetryClassification.Transient,
            _ => RetryClassification.Permanent
        };
    }

    public bool ShouldRetry(Exception exception, int attempt, int? maxAttempts = null, CancellationToken cancellationToken = default)
    {
        var limit = maxAttempts ?? MaxAttempts;
        if (attempt >= limit)
            return false;

        if (cancellationToken.IsCancellationRequested)
            return false;

        return Classify(exception, cancellationToken) == RetryClassification.Transient;
    }

    public TimeSpan GetDelay(int attempt)
    {
        var cappedAttempt = Math.Max(1, attempt);
        var ms = BaseDelayMs * (1 << Math.Min(cappedAttempt - 1, 5));
        return TimeSpan.FromMilliseconds(Math.Max(0, ms));
    }

    private static Exception Unwrap(Exception exception)
    {
        while (exception.InnerException is not null
               && exception is not HttpRequestException
               && exception is not TimeoutException
               && exception is not IOException
               && exception is not TaskCanceledException)
        {
            exception = exception.InnerException;
        }

        return exception;
    }
}
