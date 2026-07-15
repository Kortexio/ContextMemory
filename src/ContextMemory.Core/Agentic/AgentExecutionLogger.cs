using System.Diagnostics;
using System.Text;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Localization;
using ContextMemory.Core.Models;
using ContextMemory.Core.Session;
using Microsoft.Extensions.Logging;

namespace ContextMemory.Core.Agentic;

public sealed class AgentExecutionLogger : IAgentExecutionLogger
{
    private readonly ISessionStore _sessionStore;
    private readonly IAppConfigStore _appConfigStore;
    private readonly ILogger<AgentExecutionLogger> _logger;

    public AgentExecutionLogger(
        ISessionStore sessionStore,
        IAppConfigStore appConfigStore,
        ILogger<AgentExecutionLogger> logger)
    {
        _sessionStore = sessionStore;
        _appConfigStore = appConfigStore;
        _logger = logger;
    }

    public async Task LogAsync(
        string appId,
        string userId,
        string sessionId,
        string? userObjective,
        AgentResult result,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var lang = _appConfigStore.GetConfig(appId).DefaultLanguage;
            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm");
            var toolCount = result.Steps.Count;
            var status = AgenticMessages.ExecutionLogStatus(result, lang);
            var objective = Truncate(
                userObjective ?? AgenticMessages.ExecutionLogNoObjective(lang),
                120);

            var sb = new StringBuilder();
            sb.AppendLine(AgenticMessages.ExecutionLogHeader(timestamp, status, toolCount, result.Iterations, lang));
            sb.AppendLine($"{AgenticMessages.ExecutionLogObjectiveLabel(lang)} {objective}");
            sb.AppendLine();

            if (result.Steps.Count > 0)
            {
                sb.AppendLine(AgenticMessages.ExecutionLogStepsHeader(lang));
                foreach (var step in result.Steps)
                {
                    sb.AppendLine($"- **{step.ToolName}** (iter {step.Iteration}, {step.Duration.TotalMilliseconds:F0}ms, exit={step.ExitCode ?? 0})");
                    sb.AppendLine($"  - args: `{Truncate(step.Arguments, 200)}`");
                    sb.AppendLine($"  - output: {Truncate(step.Output, 500)}");
                }
                sb.AppendLine();
            }

            sb.AppendLine(AgenticMessages.ExecutionLogValidatedHeader(lang));
            sb.AppendLine(Truncate(result.FinalAnswer, 2000));

            await _sessionStore.ApplyWikiUpdateAsync(
                    appId,
                    userId,
                    sessionId,
                    new SessionWikiUpdate { LogEntry = sb.ToString().TrimEnd() },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log agentic execution for {AppId} session {SessionId}", appId, sessionId);
        }
    }

    private static string Truncate(string text, int maxChars) =>
        text.Length <= maxChars ? text : text[..maxChars] + "…";
}
