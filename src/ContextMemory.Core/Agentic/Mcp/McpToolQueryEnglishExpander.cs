using System.Text;

namespace ContextMemory.Core.Agentic.Mcp;

/// <summary>
/// Expands a user query with English domain synonyms so lexical MCP tool selection
/// works for non-English (esp. Portuguese) turns. Does not rewrite chat messages —
/// only the string used for scoring / catalog top-K.
/// </summary>
public static class McpToolQueryEnglishExpander
{
    /// <summary>
    /// Phrase / token map (source → English tokens to append). Longer phrases first.
    /// </summary>
    private static readonly (string Source, string English)[] Map =
    [
        ("subscrição", "subscription"),
        ("subscricao", "subscription"),
        ("assinatura", "subscription"),
        ("criar subscrição", "create subscription"),
        ("criação de subscrição", "subscription creation create"),
        ("criacao de subscricao", "subscription creation create"),
        ("regras de criação", "creation rules"),
        ("regras de criacao", "creation rules"),
        ("conta", "account"),
        ("contas", "account"),
        ("fatura", "invoice"),
        ("factura", "invoice"),
        ("faturas", "invoice"),
        ("pagamento", "payment"),
        ("pagamentos", "payment"),
        ("produto", "product"),
        ("produtos", "product"),
        ("plano de taxa", "rate plan"),
        ("rate plan", "rate plan"),
        ("cancelar", "cancel cancelled"),
        ("cancelada", "cancelled canceled"),
        ("cancelado", "cancelled canceled"),
        ("criação", "create creation"),
        ("criacao", "create creation"),
        ("criar", "create"),
        ("regras", "rules"),
        ("regra", "rule"),
        ("objeto", "object"),
        ("objetos", "objects"),
        ("consulta", "query"),
        ("consultar", "query"),
        ("resumo", "summary"),
        ("zuora", "zuora"),
        ("paccar", "paccar")
    ];

    public static string Expand(string? userQuery)
    {
        if (string.IsNullOrWhiteSpace(userQuery))
            return userQuery ?? string.Empty;

        var original = userQuery.Trim();
        var lower = RemoveDiacritics(original).ToLowerInvariant();
        var extras = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (source, english) in Map.OrderByDescending(m => m.Source.Length))
        {
            var srcNorm = RemoveDiacritics(source).ToLowerInvariant();
            if (lower.Contains(srcNorm, StringComparison.Ordinal))
            {
                foreach (var token in english.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    extras.Add(token);
            }
        }

        if (extras.Count == 0)
            return original;

        // Keep original (for any EN tokens already present) + English expansion for the selector.
        return original + " " + string.Join(' ', extras);
    }

    /// <summary>Strip combining marks so "subscrição" matches "subscricao" map keys.</summary>
    public static string RemoveDiacritics(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            var cat = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
