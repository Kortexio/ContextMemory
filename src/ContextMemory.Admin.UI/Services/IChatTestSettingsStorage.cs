using ContextMemory.Admin.UI.Models;

namespace ContextMemory.Admin.UI.Services;

public interface IChatTestSettingsStorage
{
    Task<ChatTestSettings?> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(ChatTestSettings settings, CancellationToken cancellationToken = default);
}
