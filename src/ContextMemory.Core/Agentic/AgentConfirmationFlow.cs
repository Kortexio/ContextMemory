using ContextMemory.Core.Localization;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

public sealed class AgentConfirmationFlow : IAgentConfirmationFlow
{
    private readonly IAgenticPendingStore _pendingStore;
    private readonly ISessionStore _sessionStore;

    public AgentConfirmationFlow(IAgenticPendingStore pendingStore, ISessionStore sessionStore)
    {
        _pendingStore = pendingStore;
        _sessionStore = sessionStore;
    }

    public async Task<AgentConfirmationFlowResult> TryResolvePendingAsync(
        string appId,
        string userId,
        string sessionId,
        string? lastUserMessage,
        Action<AgenticProgressEvent>? report,
        CancellationToken cancellationToken = default)
    {
        var existingPending = await _pendingStore
            .TryLoadAsync(appId, userId, sessionId, cancellationToken)
            .ConfigureAwait(false);

        if (existingPending is null)
            return AgentConfirmationFlowResult.Continue();

        if (AgenticConfirmationParser.IsDismissal(lastUserMessage))
        {
            await _pendingStore.ClearAsync(appId, userId, sessionId, cancellationToken).ConfigureAwait(false);
            return AgentConfirmationFlowResult.Resolved(
                AgentResult.Succeeded(
                    AgenticMessages.UserCancelledDestructive(existingPending.DefaultLanguage),
                    existingPending.Steps,
                    existingPending.Iteration));
        }

        if (!AgenticConfirmationParser.IsConfirmation(lastUserMessage, existingPending.PendingId))
            return AgentConfirmationFlowResult.Resolved(BuildAwaitingConfirmationResult(existingPending, report));

        await AgenticConfirmationCheckpoint
            .WriteConfirmedAsync(_sessionStore, appId, userId, sessionId, existingPending, cancellationToken)
            .ConfigureAwait(false);
        await _pendingStore.ClearAsync(appId, userId, sessionId, cancellationToken).ConfigureAwait(false);

        if (string.Equals(existingPending.Kind, AgenticPendingKinds.MaxIterations, StringComparison.OrdinalIgnoreCase))
        {
            Report(report, new AgenticProgressEvent
            {
                Phase = AgenticProgressPhase.ConfirmationReceived,
                Iteration = existingPending.Iteration,
                Detail = AgenticMessages.HumanReviewApprovedDetail(existingPending.DefaultLanguage)
            });

            return AgentConfirmationFlowResult.Resolved(
                AgentResult.Succeeded(
                    existingPending.PartialAnswer ?? AgenticMessages.PartialAnswerApproved(existingPending.DefaultLanguage),
                    existingPending.Steps,
                    existingPending.Iteration));
        }

        Report(report, new AgenticProgressEvent
        {
            Phase = AgenticProgressPhase.ConfirmationReceived,
            Iteration = existingPending.Iteration,
            ToolName = existingPending.ToolName,
            Detail = AgenticMessages.ConfirmationReceived(existingPending.ToolName, new AppRuntimeConfig { AppId = "_", DefaultLanguage = existingPending.DefaultLanguage })
        });

        return AgentConfirmationFlowResult.ResumeFrom(
            existingPending,
            existingPending.Messages.ToList(),
            existingPending.Steps.ToList());
    }

    private static AgentResult BuildAwaitingConfirmationResult(
        AgenticPendingState pending,
        Action<AgenticProgressEvent>? report)
    {
        Report(report, new AgenticProgressEvent
        {
            Phase = AgenticProgressPhase.AwaitingConfirmation,
            Iteration = pending.Iteration,
            ToolName = pending.ToolName,
            Detail = AgenticConfirmationParser.BuildConfirmationPrompt(pending)
        });

        return AgentResult.AwaitingHumanConfirmation(
            AgenticConfirmationParser.BuildConfirmationPrompt(pending),
            pending.PendingId,
            pending.Steps,
            pending.Iteration,
            pending.Kind);
    }

    private static void Report(Action<AgenticProgressEvent>? report, AgenticProgressEvent evt) =>
        report?.Invoke(evt);
}
