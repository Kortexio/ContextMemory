using System.Text;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Localization;

namespace ContextMemory.Core.WebSearch;

public static class WebSearchFormatter
{
    public static string ToMarkdown(WebSearchResult result, int maxChars, string? userQuery = null, string? language = null)
    {
        maxChars = Math.Max(256, maxChars);
        if (result.Hits.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine(TenantLocale.Select(
            language,
            $"## Web search ({result.RetrievedAt:yyyy-MM-ddTHH:mm:ssZ} · {result.Provider})",
            $"## Pesquisa web ({result.RetrievedAt:yyyy-MM-ddTHH:mm:ssZ} · {result.Provider})"));
        AppendResponseRules(sb, result.RetrievedAt, userQuery, language);
        sb.AppendLine();

        var index = 1;
        foreach (var hit in result.Hits)
        {
            var block = FormatHit(index, hit, language);
            if (sb.Length + block.Length > maxChars)
            {
                if (sb.Length > 200)
                    break;

                var room = maxChars - sb.Length - 20;
                if (room > 80)
                {
                    var truncated = TenantLocale.Select(language, "\n\n_(… truncated)_\n", "\n\n_(… truncado)_\n");
                    sb.Append(block[..Math.Min(block.Length, room)]).Append(truncated);
                }
                break;
            }

            sb.Append(block);
            index++;
        }

        return sb.ToString().TrimEnd();
    }

    public static string ToMaintainerSection(WebSearchResult result, string? language = null)
    {
        if (result.Hits.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine(TenantLocale.Select(
            language,
            "WEB RESULTS FROM THIS TURN (persist verifiable facts with source):",
            "RESULTADOS WEB DESTE TURNO (persistir factos verificáveis com fonte):"));
        sb.AppendLine(TenantLocale.Select(
            language,
            $"Retrieved: {result.RetrievedAt:yyyy-MM-dd} · Provider: {result.Provider}",
            $"Consultado: {result.RetrievedAt:yyyy-MM-dd} · Provider: {result.Provider}"));
        sb.AppendLine();

        var index = 1;
        foreach (var hit in result.Hits)
        {
            sb.AppendLine($"{index}. **{hit.Title.Trim()}** — {hit.Url}");
            if (hit.PublishedAt is not null)
            {
                sb.AppendLine(TenantLocale.Select(
                    language,
                    $"   _(published: {hit.PublishedAt:yyyy-MM-dd})_",
                    $"   _(publicado: {hit.PublishedAt:yyyy-MM-dd})_"));
            }
            sb.AppendLine($"   {hit.Snippet.Trim()}");
            sb.AppendLine();
            index++;
        }

        return sb.ToString().TrimEnd();
    }

    private static string FormatHit(int index, WebSearchHit hit, string? language)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{index}. **{hit.Title.Trim()}** — {hit.Url}");
        if (hit.PublishedAt is not null)
        {
            sb.AppendLine(TenantLocale.Select(
                language,
                $"   _(published: {hit.PublishedAt:yyyy-MM-dd})_",
                $"   _(publicado: {hit.PublishedAt:yyyy-MM-dd})_"));
        }
        sb.AppendLine($"   {hit.Snippet.Trim()}");
        sb.AppendLine();
        return sb.ToString();
    }

    private static void AppendResponseRules(StringBuilder sb, DateTimeOffset retrievedAt, string? userQuery, string? language)
    {
        if (TenantLocale.IsPortuguese(language))
        {
            sb.AppendLine(
                "Esta secção é a **única** fonte válida para actualidade e factos posteriores à wiki. "
                + "Responde de forma directa e assertiva.");
            sb.AppendLine();
            sb.AppendLine("Regras obrigatórias:");
            sb.AppendLine(
                $"- Se a pergunta pede um período (ex.: «este mês», «em 2026») e estes excertos **não** o cobrem, "
                + $"diz só: «Com base na pesquisa web de {retrievedAt:yyyy-MM-dd}, não encontrei…» — "
                + "**não** completes com factos da wiki.");
            sb.AppendLine("- **Nunca** apresentes conhecimento da wiki como se viesse desta pesquisa.");
            sb.AppendLine("- Cada facto de actualidade deve ter etiqueta `[web]` e URL entre parênteses.");
            sb.AppendLine("- Se os excertos forem insuficientes, admite-o — não inventes nem extrapoles.");
            if (!string.IsNullOrWhiteSpace(userQuery))
                sb.AppendLine($"- Pergunta a responder (prioridade): «{userQuery.Trim()}»");
            return;
        }

        sb.AppendLine(
            "This section is the **only** valid source for freshness and facts after the wiki. "
            + "Answer directly and assertively.");
        sb.AppendLine();
        sb.AppendLine("Mandatory rules:");
        sb.AppendLine(
            $"- If the question asks for a period (e.g. «this month», «in 2026») and these excerpts **do not** cover it, "
            + $"say only: «Based on web search as of {retrievedAt:yyyy-MM-dd}, I did not find…» — "
            + "**do not** fill gaps with wiki facts.");
        sb.AppendLine("- **Never** present wiki knowledge as if it came from this search.");
        sb.AppendLine("- Every freshness fact must have a `[web]` tag and URL in parentheses.");
        sb.AppendLine("- If excerpts are insufficient, admit it — do not invent or extrapolate.");
        if (!string.IsNullOrWhiteSpace(userQuery))
            sb.AppendLine($"- Question to answer (priority): «{userQuery.Trim()}»");
    }
}
