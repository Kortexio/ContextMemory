using ContextMemory.Core.Contracts;
using ContextMemory.Core.Localization;
using ContextMemory.Core.Models;
using ContextMemory.Core.Session;

namespace ContextMemory.Core.Agentic;

public static class AgenticConfirmationCheckpoint
{
    public static Task WritePendingAsync(
        ISessionStore sessionStore,
        string appId,
        string userId,
        string sessionId,
        AgenticPendingState pending,
        CancellationToken cancellationToken = default)
    {
        var lang = pending.DefaultLanguage;
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm");
        var status = TenantLocale.Select(lang, "confirmation pending", "confirmação pendente");
        var argsLabel = TenantLocale.Select(lang, "**Arguments:**", "**Argumentos:**");
        var iterLabel = TenantLocale.Select(lang, "**Iteration:**", "**Iteração:**");
        var blocked = TenantLocale.Select(
            lang,
            "_Execution blocked until human confirmation._",
            "_Execução bloqueada até confirmação humana._");

        var entry =
            $"## [{timestamp}] agentic-checkpoint | {status}\n"
            + $"**PendingId:** `{pending.PendingId}`\n"
            + $"**Tool:** `{pending.ToolName}`\n"
            + $"**Keyword:** `{pending.MatchedKeyword}`\n"
            + $"{argsLabel} `{pending.Arguments}`\n"
            + $"{iterLabel} {pending.Iteration}\n"
            + blocked;

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
        var lang = pending.DefaultLanguage;
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm");
        var status = TenantLocale.Select(lang, "confirmation received", "confirmação recebida");
        var authorized = TenantLocale.Select(
            lang,
            "_Action authorized — executing._",
            "_Ação autorizada — a executar._");

        var entry =
            $"## [{timestamp}] agentic-checkpoint | {status}\n"
            + $"**PendingId:** `{pending.PendingId}`\n"
            + $"**Tool:** `{pending.ToolName}`\n"
            + authorized;

        return sessionStore.ApplyWikiUpdateAsync(
            appId,
            userId,
            sessionId,
            new SessionWikiUpdate { LogEntry = entry },
            cancellationToken);
    }
}
