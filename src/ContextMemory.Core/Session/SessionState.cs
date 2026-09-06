namespace ContextMemory.Core.Session;

/// <summary>
/// Formal session lifecycle states.
/// </summary>
public enum SessionState
{
    Created = 0,
    Active = 1,
    Paused = 2,
    Completed = 3,
    Archived = 4
}

/// <summary>
/// Helpers for session lifecycle transitions.
/// </summary>
public static class SessionLifecycle
{
    private static readonly Dictionary<SessionState, HashSet<SessionState>> Allowed = new()
    {
        [SessionState.Created] = [SessionState.Active, SessionState.Archived],
        [SessionState.Active] = [SessionState.Paused, SessionState.Completed, SessionState.Archived],
        [SessionState.Paused] = [SessionState.Active, SessionState.Completed, SessionState.Archived],
        [SessionState.Completed] = [SessionState.Archived],
        [SessionState.Archived] = []
    };

    public static bool CanTransition(SessionState from, SessionState to) =>
        from == to || (Allowed.TryGetValue(from, out var next) && next.Contains(to));

    public static SessionState Transition(SessionState from, SessionState to)
    {
        if (!CanTransition(from, to))
            throw new InvalidOperationException($"Invalid session transition: {from} → {to}");
        return to;
    }
}
