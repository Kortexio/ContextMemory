using System.Collections.Concurrent;

namespace ContextMemory.Core.Engine;

public sealed class MessageIdTracker : Contracts.IMessageIdTracker
{
    private readonly ConcurrentDictionary<string, string> _lastByUser = new(StringComparer.Ordinal);

    public string CreateAndTrack(string appId, string userId)
    {
        var messageId = Guid.NewGuid().ToString("N");
        _lastByUser[Key(appId, userId)] = messageId;
        return messageId;
    }

    public bool TryGetLast(string appId, string userId, out string? messageId) =>
        _lastByUser.TryGetValue(Key(appId, userId), out messageId);

    private static string Key(string appId, string userId) => $"{appId}:{userId}";
}
