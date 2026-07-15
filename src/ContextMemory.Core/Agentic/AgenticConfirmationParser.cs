using ContextMemory.Core.Localization;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

public static class AgenticConfirmationParser
{
    private static readonly string[] ConfirmationPhrases =
    [
        "confirm",
        "confirmo",
        "confirmar",
        "approve",
        "aprovo",
        "aprovado",
        "autorizo",
        "podes prosseguir",
        "pode prosseguir",
        "yes, proceed",
        "yes proceed",
        "i confirm",
        "i approve"
    ];

    private static readonly string[] DismissalPhrases =
    [
        "cancel",
        "cancelo",
        "cancelar",
        "cancela",
        "não confirmo",
        "nao confirmo",
        "rejeito",
        "reject",
        "abort"
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

        if (trimmed.Equals("sim", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("aprovo", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("approve", StringComparison.OrdinalIgnoreCase))
            return true;

        return ConfirmationPhrases.Any(p => trimmed.Contains(p, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsDismissal(string? message) =>
        !string.IsNullOrWhiteSpace(message)
        && DismissalPhrases.Any(p => message.Contains(p, StringComparison.OrdinalIgnoreCase));

    public static string BuildConfirmationPrompt(AgenticPendingState pending) =>
        AgenticMessages.BuildConfirmationPrompt(pending, ConfigFromPending(pending));

    private static AppRuntimeConfig ConfigFromPending(AgenticPendingState pending) =>
        new() { AppId = "_", DefaultLanguage = pending.DefaultLanguage };
}
