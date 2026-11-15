using ContextMemory.Core.Agentic;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using ContextMemory.Infrastructure.Agentic;

namespace ContextMemory.Bench;

/// <summary>
/// Wraps <see cref="GlobalWikiToolExecutor"/> with <see cref="BenchStickyWikiGate"/> for CM4.
/// </summary>
internal sealed class BenchStickyWikiExecutor : IToolExecutor
{
    private readonly GlobalWikiToolExecutor _inner;
    private readonly BenchStickyWikiGate _gate;

    public BenchStickyWikiExecutor(GlobalWikiToolExecutor inner, BenchStickyWikiGate gate)
    {
        _inner = inner;
        _gate = gate;
    }

    public bool CanExecute(string toolName, AppRuntimeConfig runtimeConfig) =>
        _inner.CanExecute(toolName, runtimeConfig);

    public async Task<ToolExecutionResult> ExecuteAsync(
        OllamaToolCall toolCall,
        string appId,
        AppRuntimeConfig runtimeConfig,
        CancellationToken cancellationToken = default)
    {
        if (_gate.ShouldReturnNoise(toolCall.Function.Name, toolCall.Function.Arguments))
        {
            return new ToolExecutionResult
            {
                Output = BenchStickyWikiGate.NoiseOutput(toolCall.Function.Name),
                ExitCode = 0
            };
        }

        return await _inner.ExecuteAsync(toolCall, appId, runtimeConfig, cancellationToken)
            .ConfigureAwait(false);
    }
}
