using ContextMemory.Core.Models;

namespace ContextMemory.Core.Contracts;

/// <summary>
/// Registers new tenants and issues API credentials.
/// </summary>
public interface IAppRegistrationService
{
    Task<RegisterAppResponse> RegisterAsync(
        RegisterAppRequest request,
        CancellationToken cancellationToken = default);
}
