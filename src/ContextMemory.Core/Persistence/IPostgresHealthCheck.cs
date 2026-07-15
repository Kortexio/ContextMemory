namespace ContextMemory.Core.Persistence;

public interface IPostgresHealthCheck
{
    Task<bool> CanConnectAsync(CancellationToken cancellationToken = default);
}
