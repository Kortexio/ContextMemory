using ContextMemory.Core.Models;

namespace ContextMemory.Core.Contracts;

public interface IGlobalWikiStore
{
    /// <summary>Returns the active revision of a document, or null.</summary>
    Task<GlobalWikiDocument?> GetAsync(string appId, string documentId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GlobalWikiDocument>> ListAsync(
        string appId,
        string? sourceId = null,
        int offset = 0,
        int limit = 50,
        bool includeSuperseded = false,
        CancellationToken cancellationToken = default);

    Task<int> CountAsync(
        string appId,
        string? sourceId = null,
        bool includeSuperseded = false,
        CancellationToken cancellationToken = default);

    Task<GlobalWikiUpsertResult> UpsertAsync(
        string appId,
        string documentId,
        GlobalWikiUpsertRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Soft-deletes the active revision (closes validity window). Does not erase history.</summary>
    Task<bool> DeleteAsync(string appId, string documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Documents valid at <paramref name="asOf"/> (default: UtcNow).
    /// When asOf is null, returns active revisions only.
    /// </summary>
    Task<IReadOnlyList<GlobalWikiDocument>> GetAllForQueryAsync(
        string appId,
        string? sourceId = null,
        DateTimeOffset? asOf = null,
        CancellationToken cancellationToken = default);

    /// <summary>Ranked search over documents valid at asOf. Implementations may use FTS or in-memory scoring.</summary>
    Task<IReadOnlyList<GlobalWikiDocument>> SearchAsync(
        string appId,
        string query,
        DateTimeOffset? asOf = null,
        string? sourceId = null,
        int topK = 50,
        bool digestOnly = false,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GlobalWikiDocument>> ListRevisionsAsync(
        string appId,
        string documentId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GlobalWikiDocument>> ListAuditAsync(
        string appId,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken cancellationToken = default);
}
