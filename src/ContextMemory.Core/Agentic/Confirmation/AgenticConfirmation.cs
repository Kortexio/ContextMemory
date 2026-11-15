using ContextMemory.Core.Contracts;
using ContextMemory.Core.Localization;
using ContextMemory.Core.Models;
using ContextMemory.Core.Session;

namespace ContextMemory.Core.Agentic;

/// <summary>
/// HITL confirmation: detect confirm/cancel tokens, build prompts, write session checkpoints.
/// </summary>
public static class AgenticConfirmation
{
    private static readonly string[] ConfirmationPhrases =
    [
        "confirm",
        "approve",
        "yes, proceed",
        "yes proceed",
        "i confirm",
        "i approve"
    ];

    private static readonly string[] DismissalPhrases =
    [
        "cancel",
        "reject",
        "abort",
        "deny",
        "do not proceed"
    ];

    public static bool IsConfirmation(string? message, string pendingId)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        var trimmed = message.Trim();
        if (trimmed.Contains($"[CONFIRM:{pendingId}]", StringComparison.OrdinalIgnoreCase))
            return true;

        if (trimmed.Contains($"CONFIRM {pendingId}", StringComparison.OrdinalIgnoreCase))
            return true;

        if (trimmed.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("approve", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("confirm", StringComparison.OrdinalIgnoreCase))
            return true;

        return ConfirmationPhrases.Any(p => trimmed.Contains(p, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsDismissal(string? message) =>
        !string.IsNullOrWhiteSpace(message)
        && DismissalPhrases.Any(p => message.Contains(p, StringComparison.OrdinalIgnoreCase));

    public static string BuildPrompt(AgenticPendingState pending) =>
        AgenticMessages.BuildConfirmationPrompt(
            pending,
            new AppRuntimeConfig { AppId = "_", DefaultLanguage = pending.DefaultLanguage });

    public static Task WritePendingAsync(
        ISessionStore sessionStore,
        string appId,
        string userId,
        string sessionId,
        AgenticPendingState pending,
        CancellationToken cancellationToken = default)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm");
        var entry =
            $"## [{timestamp}] agentic-checkpoint | confirmation pending\n"
            + $"**PendingId:** `{pending.PendingId}`\n"
            + $"**Tool:** `{pending.ToolName}`\n"
            + $"**Keyword:** `{pending.MatchedKeyword}`\n"
            + $"**Arguments:** `{pending.Arguments}`\n"
            + $"**Iteration:** {pending.Iteration}\n"
            + "_Execution blocked until human confirmation._";

        return sessionStore.ApplyWikiUpdateAsync(
            appId,
            userId,
            sessionId,
            new SessionWikiUpdate { LogEntry = entry },
            cancellationToken);
    }

    public static Task WriteConfirmedAsync(
        ISessionStore sessionStore,
        string appId,
        string userId,
        string sessionId,
        AgenticPendingState pending,
        CancellationToken cancellationToken = default)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm");
        var entry =
            $"## [{timestamp}] agentic-checkpoint | confirmation received\n"
            + $"**PendingId:** `{pending.PendingId}`\n"
            + $"**Tool:** `{pending.ToolName}`\n"
            + "_Action authorized — executing._";

        return sessionStore.ApplyWikiUpdateAsync(
            appId,
            userId,
            sessionId,
            new SessionWikiUpdate { LogEntry = entry },
            cancellationToken);
    }
}
