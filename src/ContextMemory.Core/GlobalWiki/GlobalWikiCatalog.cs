namespace ContextMemory.Core.GlobalWiki;

public static class GlobalWikiCatalog
{
    public const string DocumentId = "wiki:catalog";
    public const string Title = "Knowledge catalog";

    /// <summary>Tenant-authored acronym ↔ expansion pairs used by lexical search.</summary>
    public const string GlossaryDocumentId = "wiki:glossary";
    public const string GlossaryTitle = "Acronym glossary";

    public static bool IsCatalogDocument(string? documentId) =>
        !string.IsNullOrWhiteSpace(documentId)
        && documentId.StartsWith("wiki:catalog", StringComparison.OrdinalIgnoreCase);

    public static bool IsGlossaryDocument(string? documentId) =>
        string.Equals(documentId, GlossaryDocumentId, StringComparison.OrdinalIgnoreCase);

    public static bool IsReservedDocument(string? documentId) =>
        IsCatalogDocument(documentId) || IsGlossaryDocument(documentId);
}
