using ContextMemory.Admin.UI.Models;

namespace ContextMemory.Admin.UI.Services;

public interface IAdminSettingsStorage
{
    Task<AdminSettings?> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AdminSettings settings, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}
