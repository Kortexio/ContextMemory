using ContextMemory.Admin.UI.Models;

namespace ContextMemory.Admin.UI.Services;

public sealed class AdminSession
{
    private readonly IAdminSettingsStorage _storage;

    public AdminSession(IAdminSettingsStorage storage) => _storage = storage;

    public AdminSettings Settings { get; private set; } = new();
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Settings.MasterKey)
        && !string.IsNullOrWhiteSpace(Settings.ApiBaseUrl);

    public event Action? Changed;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Settings = await _storage.LoadAsync(cancellationToken).ConfigureAwait(false) ?? new AdminSettings();
        Changed?.Invoke();
    }

    public async Task SaveAsync(AdminSettings settings, CancellationToken cancellationToken = default)
    {
        Settings = settings;
        await _storage.SaveAsync(settings, cancellationToken).ConfigureAwait(false);
        Changed?.Invoke();
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        Settings = new AdminSettings();
        await _storage.ClearAsync(cancellationToken).ConfigureAwait(false);
        Changed?.Invoke();
    }
}
