using ContextMemory.Core.Models;

namespace ContextMemory.Core.Utilities;

public static class TokenEstimator
{
    public static int Estimate(string? text) =>
        string.IsNullOrEmpty(text) ? 0 : Math.Max(1, text.Length / 4);

    public static int Estimate(IEnumerable<OllamaMessage> messages) =>
        messages.Sum(m => Estimate(m.Content));

    public static int EstimateFromByteLength(long byteLength) =>
        byteLength > 0 ? Math.Max(1, (int)(byteLength / 4)) : 1;
}
