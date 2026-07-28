namespace ContextMemory.Core.GlobalWiki;

public static class GlobalWikiCatalog
{
    public const string DocumentId = "wiki:catalog";
    public const string Title = "Knowledge catalog";

    public static bool IsCatalogDocument(string? documentId) =>
        !string.IsNullOrWhiteSpace(documentId)
        && documentId.StartsWith("wiki:catalog", StringComparison.OrdinalIgnoreCase);
}
