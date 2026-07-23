using ContextMemory.Core.Models;

namespace ContextMemory.Core.Contracts;

public interface IGlobalWikiStore
{
    Task<GlobalWikiDocument?> GetAsync(string appId, string documentId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GlobalWikiDocument>> ListAsync(
        string appId,
        string? sourceId = null,
        int offset = 0,
        int limit = 50,
        CancellationToken cancellationToken = default);

    Task<int> CountAsync(string appId, string? sourceId = null, CancellationToken cancellationToken = default);

    Task<GlobalWikiUpsertResult> UpsertAsync(
        string appId,
        string documentId,
        GlobalWikiUpsertRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string appId, string documentId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GlobalWikiDocument>> GetAllForQueryAsync(
        string appId,
        string? sourceId = null,
        CancellationToken cancellationToken = default);
}
