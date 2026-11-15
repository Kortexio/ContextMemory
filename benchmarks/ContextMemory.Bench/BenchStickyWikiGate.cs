using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ContextMemory.Bench;

/// <summary>
/// CM4 adversarial gate: the first distinct wiki query in a session returns noise;
/// only a <em>different</em> query hydrates the real wiki (forces pivot / anti-stuck).
/// </summary>
internal sealed partial class BenchStickyWikiGate
{
    private readonly object _lock = new();
    private string? _sessionId;
    private readonly HashSet<string> _distinctFingerprints = new(StringComparer.Ordinal);
    private bool _unlocked;

    public bool Enabled { get; private set; }

    public void BeginSession(string sessionId)
    {
        lock (_lock)
        {
            _sessionId = sessionId;
            _distinctFingerprints.Clear();
            _unlocked = false;
            Enabled = true;
        }
    }

    public void EndSession()
    {
        lock (_lock)
        {
            Enabled = false;
            _sessionId = null;
            _distinctFingerprints.Clear();
            _unlocked = false;
        }
    }

    /// <summary>
    /// Returns true when the executor should return noise instead of real wiki content.
    /// </summary>
    public bool ShouldReturnNoise(string toolName, string? argumentsJson)
    {
        lock (_lock)
        {
            if (!Enabled || string.IsNullOrEmpty(_sessionId))
                return false;

            var fp = Fingerprint(toolName, argumentsJson);
            var isNew = _distinctFingerprints.Add(fp);

            // First distinct query in the session → always noise (forces refine).
            if (_distinctFingerprints.Count == 1)
                return true;

            // A new distinct query after the first unlocks real wiki for this and later calls.
            if (isNew)
            {
                _unlocked = true;
                return false;
            }

            // Same fingerprint again (if it slipped past the duplicate guard): keep noise until unlocked.
            return !_unlocked;
        }
    }

    public static string NoiseOutput(string toolName) =>
        "No useful matches for that lookup. The result is empty/noisy — "
        + "do NOT repeat the same "
        + toolName
        + " call. Change the query or pattern substantially, then try again.";

    private static string Fingerprint(string toolName, string? argumentsJson)
    {
        var name = (toolName ?? string.Empty).Trim().ToLowerInvariant();
        var args = NormalizeArgs(argumentsJson);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(name + "|" + args));
        return Convert.ToHexString(bytes.AsSpan(0, 8));
    }

    private static string NormalizeArgs(string? argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
            return "{}";
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return CollapseWs(argumentsJson);

            // Prefer query/pattern fields (wiki_search / wiki_grep).
            if (TryGetIgnoreCase(doc.RootElement, "query", out var q) && q.ValueKind == JsonValueKind.String)
                return "query=" + CollapseWs(q.GetString() ?? string.Empty).ToLowerInvariant();
            if (TryGetIgnoreCase(doc.RootElement, "pattern", out var p) && p.ValueKind == JsonValueKind.String)
                return "pattern=" + CollapseWs(p.GetString() ?? string.Empty).ToLowerInvariant();

            return CollapseWs(argumentsJson).ToLowerInvariant();
        }
        catch
        {
            return CollapseWs(argumentsJson).ToLowerInvariant();
        }
    }

    private static bool TryGetIgnoreCase(JsonElement obj, string name, out JsonElement value)
    {
        if (obj.TryGetProperty(name, out value))
            return true;
        foreach (var prop in obj.EnumerateObject())
        {
            if (prop.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    private static string CollapseWs(string s) =>
        Whitespace().Replace(s.Trim(), " ");

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
