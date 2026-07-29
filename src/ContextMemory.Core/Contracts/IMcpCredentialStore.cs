using ContextMemory.Core.Agentic.Mcp;

namespace ContextMemory.Core.Contracts;

public interface IMcpCredentialStore
{
    Task<McpCredentialRecord?> GetAsync(
        string appId,
        string integrationName,
        string? credentialRef,
        CancellationToken cancellationToken = default);

    Task UpsertAsync(
        McpCredentialRecord record,
        CancellationToken cancellationToken = default);
}
