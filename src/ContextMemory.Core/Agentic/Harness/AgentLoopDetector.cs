using System.Security.Cryptography;
using System.Text;

namespace ContextMemory.Core.Agentic.Harness;

/// <summary>
/// Global stall detector (CodeForge loopDetector). Wiki-budget rules stay in
/// <c>AgenticDuplicateToolCallGuard</c> — this only watches fingerprint+result streaks.
/// </summary>
public sealed class AgentLoopDetector
{
    private readonly int _stallThreshold;
    private readonly Dictionary<string, int> _firstSeen = new(StringComparer.Ordinal);
    private readonly List<bool> _stepCalls = [];
    private int _consecutiveIdenticalSteps;
    private int _unchangedStreak;

    public AgentLoopDetector(int stallThreshold = 3)
    {
        _stallThreshold = Math.Max(2, stallThreshold);
    }

    public int ConsecutiveIdenticalSteps => _consecutiveIdenticalSteps;
    public int UnchangedStreak => _unchangedStreak;

    public void SetUnchangedStreak(int n) => _unchangedStreak = Math.Max(0, n);

    public string RecordCall(string toolName, string? argumentsJson, string? resultText)
    {
        var fp = Fingerprint(toolName, argumentsJson);
        var rh = HashResult(resultText);
        var key = fp + "\0" + rh;
        var isRepeat = _firstSeen.ContainsKey(key);
        if (!isRepeat)
            _firstSeen[key] = 1;
        _stepCalls.Add(isRepeat);
        return isRepeat ? "identical-call" : string.Empty;
    }

    public void EndStep()
    {
        if (_stepCalls.Count > 0 && _stepCalls.All(isRepeat => isRepeat))
            _consecutiveIdenticalSteps++;
        else
            _consecutiveIdenticalSteps = 0;
        _stepCalls.Clear();
    }

    public void ResetIdenticalStreak() => _consecutiveIdenticalSteps = 0;

    public bool ShouldStop() => StallKind() is not null;

    public string? StallKind()
    {
        if (_consecutiveIdenticalSteps >= _stallThreshold)
            return "identical";
        if (_unchangedStreak >= _stallThreshold)
            return "unchanged";
        return null;
    }

    public string Cause() => StallKind() switch
    {
        "identical" => $"identical tool+result streak ({_consecutiveIdenticalSteps})",
        "unchanged" => $"unchanged UI/result streak ({_unchangedStreak})",
        _ => string.Empty
    };

    public static string Fingerprint(string toolName, string? argumentsJson)
    {
        var name = (toolName ?? string.Empty).Trim().ToLowerInvariant();
        var args = NormalizeArgs(argumentsJson);
        return name + "|" + args;
    }

    public static string HashResult(string? text)
    {
        var s = text ?? string.Empty;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(s));
        return Convert.ToHexString(bytes.AsSpan(0, 8)).ToLowerInvariant();
    }

    private static string NormalizeArgs(string? argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
            return "{}";
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(argumentsJson);
            return doc.RootElement.GetRawText();
        }
        catch
        {
            return argumentsJson.Trim();
        }
    }
}
