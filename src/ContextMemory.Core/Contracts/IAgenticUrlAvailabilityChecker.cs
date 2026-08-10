namespace ContextMemory.Core.Contracts;

public interface IAgenticUrlAvailabilityChecker
{
    /// <summary>
    /// Returns unreachable URL strings (empty if all reachable or none to check).
    /// Skips private/loopback hosts. Uses short HEAD then GET timeout.
    /// </summary>
    Task<IReadOnlyList<string>> FindUnreachableUrlsAsync(
        IReadOnlyList<string> urls,
        int timeoutMs,
        CancellationToken cancellationToken = default);
}
