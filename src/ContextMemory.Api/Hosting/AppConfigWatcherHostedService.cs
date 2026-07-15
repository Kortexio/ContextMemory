using ContextMemory.Core.Contracts;

namespace ContextMemory.Api.Hosting;

public sealed class AppConfigWatcherHostedService : IHostedService, IDisposable
{
    private readonly IAppConfigStore _appConfigStore;
    private readonly ILogger<AppConfigWatcherHostedService> _logger;
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly TimeSpan _debounceDelay = TimeSpan.FromMilliseconds(400);
    private readonly Dictionary<string, Timer> _debounceTimers = new();

    public AppConfigWatcherHostedService(
        IAppConfigStore appConfigStore,
        ILogger<AppConfigWatcherHostedService> logger)
    {
        _appConfigStore = appConfigStore;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var root = _appConfigStore.ProfilesRoot;
        Directory.CreateDirectory(root);

        var watcher = new FileSystemWatcher(root)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime
        };

        FileSystemEventHandler handler = (_, args) =>
        {
            var appId = ResolveAppId(root, args.FullPath);
            if (appId is not null)
                ScheduleReload(appId);
        };

        watcher.Changed += handler;
        watcher.Created += handler;
        watcher.Deleted += handler;
        watcher.Renamed += (_, args) =>
        {
            var appId = ResolveAppId(root, args.FullPath);
            if (appId is not null)
                ScheduleReload(appId);
        };
        watcher.EnableRaisingEvents = true;
        _watchers.Add(watcher);

        _logger.LogInformation("AppConfig FileSystemWatcher active at {Root}", root);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var watcher in _watchers.ToArray())
            watcher.Dispose();
        _watchers.Clear();

        foreach (var timer in _debounceTimers.Values.ToArray())
            timer?.Dispose();
        _debounceTimers.Clear();

        return Task.CompletedTask;
    }

    private void ScheduleReload(string appId)
    {
        if (_debounceTimers.TryGetValue(appId, out var existing))
        {
            existing.Change(_debounceDelay, Timeout.InfiniteTimeSpan);
            return;
        }

        var timer = new Timer(
            async _ =>
            {
                try
                {
                    await _appConfigStore.ReloadAsync(appId).ConfigureAwait(false);
                    _logger.LogInformation("App config hot-reloaded for {AppId}", appId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to reload app config for {AppId}", appId);
                }
            },
            null,
            _debounceDelay,
            Timeout.InfiniteTimeSpan);

        _debounceTimers[appId] = timer;
    }

    private static string? ResolveAppId(string profilesRoot, string fullPath)
    {
        var relative = Path.GetRelativePath(profilesRoot, fullPath);
        var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Length > 0 && !parts[0].StartsWith('.') ? parts[0] : null;
    }

    public void Dispose() => StopAsync(CancellationToken.None).GetAwaiter().GetResult();
}
