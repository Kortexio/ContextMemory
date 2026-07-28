using System.Text.Json.Serialization;

namespace ContextMemory.Core.Models;

public sealed class GlobalWikiDocument
{
    public required string AppId { get; init; }
    public required string DocumentId { get; init; }
    public required string Slug { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string SourceId { get; init; } = string.Empty;
    public Dictionary<string, string> Metadata { get; init; } = new();
    public string ContentHash { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed class GlobalWikiUpsertRequest
{
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("content")]
    public required string Content { get; init; }

    [JsonPropertyName("summary")]
    public string? Summary { get; init; }

    [JsonPropertyName("sourceId")]
    public string? SourceId { get; init; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, string>? Metadata { get; init; }

    [JsonPropertyName("slug")]
    public string? Slug { get; init; }
}

public sealed class GlobalWikiUpsertResult
{
    [JsonPropertyName("appId")]
    public required string AppId { get; init; }

    [JsonPropertyName("documentId")]
    public required string DocumentId { get; init; }

    [JsonPropertyName("slug")]
    public required string Slug { get; init; }

    [JsonPropertyName("contentHash")]
    public required string ContentHash { get; init; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; init; }

    [JsonPropertyName("created")]
    public bool Created { get; init; }

    [JsonPropertyName("unchanged")]
    public bool Unchanged { get; init; }
}

public sealed class GlobalWikiBatchUpsertRequest
{
    [JsonPropertyName("documents")]
    public List<GlobalWikiBatchDocument> Documents { get; init; } = [];
}

public sealed class GlobalWikiDigestRebuildRequest
{
    /// <summary>When true, regenerates digests for every document. When false, only docs missing an LLM digest.</summary>
    [JsonPropertyName("force")]
    public bool Force { get; init; }

    [JsonPropertyName("sourceId")]
    public string? SourceId { get; init; }
}

public sealed class GlobalWikiDigestRebuildResult
{
    [JsonPropertyName("appId")]
    public required string AppId { get; init; }

    [JsonPropertyName("processed")]
    public int Processed { get; init; }

    [JsonPropertyName("updated")]
    public int Updated { get; init; }

    [JsonPropertyName("skipped")]
    public int Skipped { get; init; }

    [JsonPropertyName("catalogRefreshed")]
    public bool CatalogRefreshed { get; init; }
}

public sealed class GlobalWikiBatchDocument
{
    [JsonPropertyName("documentId")]
    public required string DocumentId { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("content")]
    public required string Content { get; init; }

    [JsonPropertyName("summary")]
    public string? Summary { get; init; }

    [JsonPropertyName("sourceId")]
    public string? SourceId { get; init; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, string>? Metadata { get; init; }

    [JsonPropertyName("slug")]
    public string? Slug { get; init; }
}

public sealed class GlobalWikiBatchUpsertResult
{
    [JsonPropertyName("results")]
    public List<GlobalWikiUpsertResult> Results { get; init; } = [];
}

public sealed class GlobalWikiListResult
{
    [JsonPropertyName("documents")]
    public List<GlobalWikiDocumentSummary> Documents { get; init; } = [];

    [JsonPropertyName("total")]
    public int Total { get; init; }

    [JsonPropertyName("offset")]
    public int Offset { get; init; }

    [JsonPropertyName("limit")]
    public int Limit { get; init; }
}

public sealed class GlobalWikiDocumentSummary
{
    [JsonPropertyName("documentId")]
    public required string DocumentId { get; init; }

    [JsonPropertyName("slug")]
    public required string Slug { get; init; }

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("summary")]
    public string Summary { get; init; } = string.Empty;

    [JsonPropertyName("sourceId")]
    public string SourceId { get; init; } = string.Empty;

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed class GlobalWikiQueryRequest
{
    [JsonPropertyName("query")]
    public required string Query { get; init; }

    [JsonPropertyName("sourceId")]
    public string? SourceId { get; init; }

    [JsonPropertyName("topK")]
    public int TopK { get; init; } = 5;

    [JsonPropertyName("budgetChars")]
    public int BudgetChars { get; init; }

    /// <summary>
    /// When true, appends a compact index of <em>matched</em> docs after page bodies if budget remains.
    /// Defaults to false so large corpora do not drown matches under an index of hundreds of pages.
    /// </summary>
    [JsonPropertyName("includeIndex")]
    public bool IncludeIndex { get; init; }
}

public sealed class GlobalWikiQueryResult
{
    [JsonPropertyName("compiledMarkdown")]
    public string CompiledMarkdown { get; init; } = string.Empty;

    [JsonPropertyName("charCount")]
    public int CharCount { get; init; }

    [JsonPropertyName("includedDocuments")]
    public int IncludedDocuments { get; init; }

    [JsonPropertyName("totalDocuments")]
    public int TotalDocuments { get; init; }

    [JsonPropertyName("truncated")]
    public bool Truncated { get; init; }

    [JsonPropertyName("matches")]
    public List<GlobalWikiMatch> Matches { get; init; } = [];
}

public sealed class GlobalWikiMatch
{
    [JsonPropertyName("documentId")]
    public required string DocumentId { get; init; }

    [JsonPropertyName("slug")]
    public required string Slug { get; init; }

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("score")]
    public double Score { get; init; }

    [JsonPropertyName("sourceId")]
    public string SourceId { get; init; } = string.Empty;
}
