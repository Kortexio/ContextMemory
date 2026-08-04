using ContextMemory.Core.Models;

namespace ContextMemory.Core.Contracts;

public interface IPlatformDefaultsStore
{
    /// <summary>
    /// Returns merged platform defaults (persisted file wins when non-empty; else options).
    /// </summary>
    PlatformDefaults Get();

    Task<PlatformDefaults> UpdateAsync(
        PlatformDefaultsPatchRequest patch,
        CancellationToken cancellationToken = default);
}
