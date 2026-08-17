using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.GlobalWiki;

/// <summary>
/// Bidirectional acronym ↔ expansion map for lexical wiki search (not embeddings).
/// Auto pairs come from digests; manual overrides live under <c>## Manual overrides</c> in <c>wiki:glossary</c>.
/// </summary>
public sealed class GlobalWikiAliasLexicon
{
    public static GlobalWikiAliasLexicon Empty { get; } = new();

    private static readonly ConcurrentDictionary<string, GlobalWikiAliasLexicon> LastByApp =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> Stopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "and", "as", "at", "by", "da", "das", "de", "do", "dos", "e", "for",
        "from", "in", "of", "on", "or", "o", "os", "the", "to", "um", "uma"
    };

    private static readonly HashSet<string> BlockedAcronyms = new(StringComparer.OrdinalIgnoreCase)
    {
        "AND", "ARE", "BUT", "FOR", "HTTP", "HTTPS", "JSON", "NOTE", "NULL", "THE", "TODO", "YOU"
    };

    private static readonly Regex Parenthetical = new(
        @"\b([A-Z][A-Z0-9]{1,7})\s*\(([^)]{3,80})\)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex EmDashPair = new(
        @"^(?:#{1,6}\s+)?(?:[-*]\s+)?([A-Z][A-Z0-9]{1,7})\s*[—–\-]\s+(.{4,80})$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex GlossaryLine = new(
        @"^(?:[-*]\s+)?(?:\|\s*)?([A-Za-z][A-Za-z0-9]{1,7})\s*(?:[:|—–]|\s-\s)\s*(.+?)(?:\s*\|)?\s*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex AliasesLine = new(
        @"^Aliases:\s*(.+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.Multiline);

    public const string AutoSectionHeader = "## Auto";
    public const string ManualSectionHeader = "## Manual overrides";

    private readonly Dictionary<string, string> _acronymToExpansion = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _expansionToAcronym = new(StringComparer.OrdinalIgnoreCase);

    public int PairCount => _acronymToExpansion.Count;

    public IReadOnlyDictionary<string, string> ExportPairs() =>
        new Dictionary<string, string>(_acronymToExpansion, StringComparer.OrdinalIgnoreCase);

    /// <summary>Harvest acronym pairs from document titles and digests (not full bodies).</summary>
    public static IReadOnlyDictionary<string, string> HarvestPairsFromDocuments(
        IEnumerable<GlobalWikiDocument> documents)
    {
        var lexicon = new GlobalWikiAliasLexicon();
        foreach (var doc in documents)
        {
            if (GlobalWikiCatalog.IsReservedDocument(doc.DocumentId))
                continue;
            lexicon.Harvest(doc.Title, doc.Summary);
        }

        return lexicon.ExportPairs();
    }

    public static string BuildGlossaryMarkdown(
        IReadOnlyDictionary<string, string> autoPairs,
        IReadOnlyDictionary<string, string> manualPairs)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {GlobalWikiCatalog.GlossaryTitle}");
        sb.AppendLine();
        sb.AppendLine(
            $"_Updated: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC · {autoPairs.Count} auto · {manualPairs.Count} manual_");
        sb.AppendLine();
        sb.AppendLine(AutoSectionHeader);
        sb.AppendLine("_Regenerated from document digests on each `digests/rebuild`. Do not edit._");
        sb.AppendLine();
        foreach (var kv in autoPairs.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            sb.AppendLine($"- {FormatGlossaryEntry(kv.Key, kv.Value)}");
        if (autoPairs.Count == 0)
            sb.AppendLine("_No acronym pairs harvested yet._");
        sb.AppendLine();
        sb.AppendLine(ManualSectionHeader);
        sb.AppendLine("_Org-wide acronyms not visible in tickets. Edit here; preserved on rebuild._");
        sb.AppendLine();
        foreach (var kv in manualPairs.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            sb.AppendLine($"- {FormatGlossaryEntry(kv.Key, kv.Value)}");
        if (manualPairs.Count == 0)
            sb.AppendLine("_None._");
        return sb.ToString().TrimEnd() + "\n";
    }

    /// <summary>Split an existing glossary into manual overrides and (ignored) auto section.</summary>
    public static (Dictionary<string, string> Manual, Dictionary<string, string> Auto) ParseGlossarySections(
        string? markdown)
    {
        var manual = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var auto = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(markdown))
            return (manual, auto);

        var hasStructuredSections = markdown.Contains(AutoSectionHeader, StringComparison.Ordinal)
            || markdown.Contains(ManualSectionHeader, StringComparison.Ordinal);

        var section = hasStructuredSections ? GlossarySection.None : GlossarySection.Manual;
        foreach (var raw in markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
                continue;
            if (line.StartsWith('#'))
            {
                if (line.StartsWith(ManualSectionHeader, StringComparison.OrdinalIgnoreCase))
                    section = GlossarySection.Manual;
                else if (line.StartsWith(AutoSectionHeader, StringComparison.OrdinalIgnoreCase))
                    section = GlossarySection.Auto;
                else if (!hasStructuredSections)
                    continue;
                else
                    continue;
                continue;
            }

            if (line.StartsWith('_') && line.EndsWith('_'))
                continue;
            if (line.StartsWith('|') && line.Contains("---", StringComparison.Ordinal))
                continue;

            var target = section switch
            {
                GlossarySection.Auto => auto,
                GlossarySection.Manual => manual,
                _ => manual
            };
            TryParseGlossaryLine(line, target);
        }

        return (manual, auto);
    }

    private enum GlossarySection
    {
        None,
        Auto,
        Manual
    }

    private static void TryParseGlossaryLine(string line, Dictionary<string, string> target)
    {
        var scratch = new GlobalWikiAliasLexicon();
        scratch.ParseGlossaryLineInto(line, target);
    }

    private void ParseGlossaryLineInto(string line, Dictionary<string, string> target)
    {
        var match = GlossaryLine.Match(line);
        if (match.Success)
        {
            AddToDictionary(match.Groups[1].Value, match.Groups[2].Value, target);
            return;
        }

        HarvestLineInto(line, target);
    }

    private void AddToDictionary(string? acronymRaw, string? expansionRaw, Dictionary<string, string> target)
    {
        var acronym = NormalizeAcronym(acronymRaw);
        var expansion = NormalizeExpansion(expansionRaw);
        if (acronym is null || expansion is null)
            return;
        if (string.Equals(acronym, expansion, StringComparison.OrdinalIgnoreCase))
            return;
        target[acronym] = expansion;
    }

    private static string FormatGlossaryEntry(string acronym, string expansion)
    {
        var particles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "de", "da", "do", "dos", "das", "of", "the", "and", "e"
        };
        var display = string.Join(
            ' ',
            expansion.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select((w, i) =>
                    i > 0 && (particles.Contains(w) || w.Length <= 3)
                        ? w
                        : char.ToUpperInvariant(w[0]) + w[1..]));
        return $"{acronym.ToUpperInvariant()}: {display}";
    }

    public static void Remember(string appId, GlobalWikiAliasLexicon lexicon)
    {
        if (string.IsNullOrWhiteSpace(appId))
            return;
        LastByApp[appId] = lexicon;
    }

    public static void Invalidate(string appId)
    {
        if (!string.IsNullOrWhiteSpace(appId))
            LastByApp.TryRemove(appId, out _);
    }

    public static GlobalWikiAliasLexicon ForApp(string appId) =>
        LastByApp.TryGetValue(appId, out var lexicon) ? lexicon : Empty;

    public static GlobalWikiAliasLexicon FromDocuments(IEnumerable<GlobalWikiDocument> documents)
    {
        var lexicon = new GlobalWikiAliasLexicon();
        foreach (var doc in documents)
        {
            if (GlobalWikiCatalog.IsGlossaryDocument(doc.DocumentId))
            {
                lexicon.ParseGlossary(doc.Content);
                continue;
            }

            lexicon.Harvest(doc.Title, doc.Summary);
            if (GlobalWikiCatalog.IsCatalogDocument(doc.DocumentId))
                lexicon.Harvest(doc.Title, doc.Content);
        }

        return lexicon;
    }

    public static GlobalWikiAliasLexicon FromHarvest(
        string? glossaryMarkdown,
        IEnumerable<(string Title, string Summary)> rows)
    {
        var lexicon = new GlobalWikiAliasLexicon();
        lexicon.ParseGlossary(glossaryMarkdown);
        foreach (var (title, summary) in rows)
            lexicon.Harvest(title, summary);
        return lexicon;
    }

    public GlobalWikiQueryExpansion Expand(string? query)
    {
        var original = query ?? string.Empty;
        var tokens = GlobalWikiScoring.Tokenize(original).ToList();
        if (tokens.Count == 0)
        {
            return new GlobalWikiQueryExpansion
            {
                OriginalQuery = original,
                OriginalPhrase = string.Empty,
                Groups = []
            };
        }

        var groups = new List<GlobalWikiSynonymGroup>();
        var consumed = new bool[tokens.Count];

        var expansions = _expansionToAcronym.Keys
            .Select(e => (Phrase: e, Parts: e.Split(' ', StringSplitOptions.RemoveEmptyEntries)))
            .Where(e => e.Parts.Length >= 2)
            .OrderByDescending(e => e.Parts.Length)
            .ToList();

        for (var i = 0; i < tokens.Count; i++)
        {
            if (consumed[i])
                continue;

            var matchedExpansion = false;
            foreach (var (phrase, parts) in expansions)
            {
                if (i + parts.Length > tokens.Count)
                    continue;
                if (consumed.Skip(i).Take(parts.Length).Any(c => c))
                    continue;
                if (!parts.SequenceEqual(tokens.Skip(i).Take(parts.Length), StringComparer.OrdinalIgnoreCase))
                    continue;

                for (var j = 0; j < parts.Length; j++)
                    consumed[i + j] = true;

                var acronym = _expansionToAcronym[phrase];
                var fullPhrase = _acronymToExpansion.TryGetValue(acronym, out var mapped)
                    ? mapped
                    : phrase;
                groups.Add(BuildGroup(fullPhrase, acronym, fullPhrase));
                matchedExpansion = true;
                break;
            }

            if (matchedExpansion)
                continue;

            consumed[i] = true;
            var token = tokens[i];
            if (_acronymToExpansion.TryGetValue(token, out var expansion))
                groups.Add(BuildGroup(token, token, expansion));
            else
                groups.Add(BuildGroup(token, acronym: null, expansionPhrase: null));
        }

        return new GlobalWikiQueryExpansion
        {
            OriginalQuery = original,
            OriginalPhrase = string.Join(' ', tokens),
            Groups = groups
        };
    }

    /// <summary>Sanitized <c>simple</c> tsquery: synonym ORs, AND across original groups.</summary>
    public static string? ToPostgresTsQuery(GlobalWikiQueryExpansion expansion)
    {
        if (expansion.Groups.Count == 0)
            return null;

        var groups = new List<string>();
        foreach (var group in expansion.Groups)
        {
            var alts = new List<string>();
            var acronym = SanitizeTsTerm(group.Acronym);
            if (acronym is not null)
                alts.Add(acronym);

            var andTerms = group.ExpansionIndexTokens
                .Select(SanitizeTsTerm)
                .Where(t => t is not null)
                .Cast<string>()
                .ToList();
            if (andTerms.Count == 1)
                alts.Add(andTerms[0]);
            else if (andTerms.Count > 1)
                alts.Add("(" + string.Join(" & ", andTerms) + ")");

            if (alts.Count == 0)
            {
                var fallback = SanitizeTsTerm(group.Canonical);
                if (fallback is not null)
                    alts.Add(fallback);
            }

            if (alts.Count == 1)
                groups.Add(alts[0]);
            else if (alts.Count > 1)
                groups.Add("(" + string.Join(" | ", alts) + ")");
        }

        return groups.Count == 0 ? null : string.Join(" & ", groups);
    }

    /// <summary>Permissive OR tsquery — used when strict AND matching returns no hits.</summary>
    public static string? ToPostgresOrTsQuery(GlobalWikiQueryExpansion expansion)
    {
        var terms = expansion.IndexTokens
            .Select(SanitizeTsTerm)
            .Where(t => t is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return terms.Count == 0 ? null : string.Join(" | ", terms);
    }

    public void ParseGlossary(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return;

        var (manual, auto) = ParseGlossarySections(markdown);
        foreach (var kv in manual)
            TryAdd(kv.Key, kv.Value);
        foreach (var kv in auto)
            TryAdd(kv.Key, kv.Value);
    }

    public void Harvest(string? title, string? text)
    {
        HarvestChunk(title);
        HarvestChunk(text);
    }

    private void HarvestChunk(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        foreach (Match match in Parenthetical.Matches(text))
            TryAdd(match.Groups[1].Value, match.Groups[2].Value);

        foreach (Match match in EmDashPair.Matches(text.Replace("\r\n", "\n", StringComparison.Ordinal)))
            TryAdd(match.Groups[1].Value, match.Groups[2].Value);

        foreach (Match match in AliasesLine.Matches(text.Replace("\r\n", "\n", StringComparison.Ordinal)))
            ParseAliasAssignments(match.Groups[1].Value);
    }

    private void HarvestLineInto(string line, Dictionary<string, string> target)
    {
        foreach (Match match in Parenthetical.Matches(line))
            AddToDictionary(match.Groups[1].Value, match.Groups[2].Value, target);

        var emDash = EmDashPair.Match(line);
        if (emDash.Success)
            AddToDictionary(emDash.Groups[1].Value, emDash.Groups[2].Value, target);
    }

    private void ParseAliasAssignments(string assignments)
    {
        foreach (var part in assignments.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            var colon = part.IndexOf(':');
            var sep = eq >= 0 && (colon < 0 || eq < colon) ? eq : colon;
            if (sep <= 0 || sep >= part.Length - 1)
                continue;
            TryAdd(part[..sep].Trim(), part[(sep + 1)..].Trim());
        }
    }

    private void TryAdd(string? acronymRaw, string? expansionRaw)
    {
        var acronym = NormalizeAcronym(acronymRaw);
        var expansion = NormalizeExpansion(expansionRaw);
        if (acronym is null || expansion is null)
            return;
        if (string.Equals(acronym, expansion, StringComparison.OrdinalIgnoreCase))
            return;

        _acronymToExpansion[acronym] = expansion;
        _expansionToAcronym[expansion] = acronym;
        var stripped = string.Join(' ', expansion.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !Stopwords.Contains(p)));
        if (stripped.Length >= 6 && !string.Equals(stripped, expansion, StringComparison.Ordinal))
            _expansionToAcronym[stripped] = acronym;
    }

    private static GlobalWikiSynonymGroup BuildGroup(string canonical, string? acronym, string? expansionPhrase)
    {
        var expansionTokens = string.IsNullOrWhiteSpace(expansionPhrase)
            ? Array.Empty<string>()
            : expansionPhrase.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(t => t.Length >= 2 && !Stopwords.Contains(t))
                .ToArray();

        var index = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { canonical };
        if (!string.IsNullOrWhiteSpace(acronym))
            index.Add(acronym);
        foreach (var token in expansionTokens)
            index.Add(token);

        return new GlobalWikiSynonymGroup
        {
            Canonical = canonical,
            Acronym = acronym?.ToLowerInvariant() ?? string.Empty,
            ExpansionPhrase = expansionPhrase ?? string.Empty,
            ExpansionIndexTokens = expansionTokens,
            IndexTokens = index.ToList()
        };
    }

    private static string? NormalizeAcronym(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var trimmed = raw.Trim().Trim('[', ']', '`', '"', '\'');
        if (trimmed.Length is < 2 or > 8)
            return null;
        if (!trimmed.All(char.IsLetterOrDigit))
            return null;
        if (trimmed.Count(char.IsLetter) < 2)
            return null;
        if (BlockedAcronyms.Contains(trimmed))
            return null;
        return trimmed.ToLowerInvariant();
    }

    private static string? NormalizeExpansion(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var trimmed = raw.Trim().TrimEnd('.', ';', ',').Trim();
        if (trimmed.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return null;
        var parts = trimmed.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return null;
        var joined = string.Join(' ', parts.Select(p => p.Trim().ToLowerInvariant()));
        return joined.Length is >= 6 and <= 80 ? joined : null;
    }

    private static string? SanitizeTsTerm(string? term)
    {
        if (string.IsNullOrWhiteSpace(term))
            return null;
        var t = term.Trim().ToLowerInvariant();
        if (t.Length is < 2 or > 40)
            return null;
        return t.All(c => char.IsAsciiLetterOrDigit(c)) ? t : null;
    }
}

public sealed class GlobalWikiQueryExpansion
{
    public required string OriginalQuery { get; init; }
    public required string OriginalPhrase { get; init; }
    public required IReadOnlyList<GlobalWikiSynonymGroup> Groups { get; init; }

    public IEnumerable<string> IndexTokens =>
        Groups.SelectMany(g => g.IndexTokens).Distinct(StringComparer.OrdinalIgnoreCase);
}

public sealed class GlobalWikiSynonymGroup
{
    public required string Canonical { get; init; }
    public required string Acronym { get; init; }
    public required string ExpansionPhrase { get; init; }
    public required IReadOnlyList<string> ExpansionIndexTokens { get; init; }
    public required IReadOnlyList<string> IndexTokens { get; init; }

    public bool Hits(string haystack)
    {
        if (!string.IsNullOrEmpty(Acronym)
            && haystack.Contains(Acronym, StringComparison.Ordinal))
            return true;
        if (!string.IsNullOrEmpty(ExpansionPhrase)
            && haystack.Contains(ExpansionPhrase, StringComparison.Ordinal))
            return true;
        return haystack.Contains(Canonical, StringComparison.Ordinal);
    }
}
