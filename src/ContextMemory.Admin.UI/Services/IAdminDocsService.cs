namespace ContextMemory.Admin.UI.Services;

public sealed record AdminDocEntry(string Slug, string Title, string FileName);

public sealed record AdminDocContent(AdminDocEntry Entry, string Html);

public interface IAdminDocsService
{
    IReadOnlyList<AdminDocEntry> ListDocuments();
    Task<AdminDocContent?> GetIndexAsync(CancellationToken cancellationToken = default);
    Task<AdminDocContent?> GetDocumentAsync(string slug, CancellationToken cancellationToken = default);
}
