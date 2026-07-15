using System.Collections.Concurrent;
using System.Text.Json;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Microsoft.Extensions.Options;

namespace ContextMemory.Infrastructure.Session;

public sealed class FileSessionStore : ISessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _sessionsRoot;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public FileSessionStore(IOptions<ContextMemoryOptions> options)
    {
        _sessionsRoot = Path.Combine(
            Path.GetFullPath(options.Value.DataPath, options.Value.ContentRootPath),
            "sessions");
        Directory.CreateDirectory(_sessionsRoot);
    }

    public async Task<SessionSnapshot> LoadAsync(
        string appId,
        string userId,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var gate = GetLock(sessionId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var dir = GetSessionDir(sessionId);
            if (!Directory.Exists(dir))
            {
                return new SessionSnapshot
                {
                    SessionPath = dir,
                    IndexMd = string.Empty,
                    LogMd = string.Empty,
                    SchemaMd = string.Empty,
                    Pages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    PageLastModified = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase),
                    Messages = []
                };
            }

            await EnsureSessionAccessAsync(dir, appId, userId, cancellationToken).ConfigureAwait(false);

            SessionWikiIndexSync.RepairSessionLayout(dir);

            var pagesDir = Path.Combine(dir, "pages");
            var pages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var pageTimes = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);

            if (Directory.Exists(pagesDir))
            {
                foreach (var file in Directory.EnumerateFiles(pagesDir, "*.md", SearchOption.TopDirectoryOnly))
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    pages[name] = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
                    pageTimes[name] = File.GetLastWriteTimeUtc(file);
                }
            }

            var messages = await LoadMessagesAsync(dir, cancellationToken).ConfigureAwait(false);

            return new SessionSnapshot
            {
                SessionPath = dir,
                IndexMd = await ReadOptionalAsync(Path.Combine(dir, "index.md"), cancellationToken).ConfigureAwait(false),
                LogMd = await ReadOptionalAsync(Path.Combine(dir, "log.md"), cancellationToken).ConfigureAwait(false),
                SchemaMd = await ReadOptionalAsync(Path.Combine(dir, "schema.md"), cancellationToken).ConfigureAwait(false),
                Pages = pages,
                PageLastModified = pageTimes,
                Messages = messages
            };
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task EnsureInitializedAsync(
        string appId,
        string userId,
        string sessionId,
        string appSchema,
        CancellationToken cancellationToken = default)
    {
        var gate = GetLock(sessionId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var dir = GetSessionDir(sessionId);
            Directory.CreateDirectory(Path.Combine(dir, "pages"));

            await WriteMetaAsync(dir, appId, userId, cancellationToken).ConfigureAwait(false);

            await EnsureFileAsync(Path.Combine(dir, "index.md"), SessionDefaults.EmptyIndex, cancellationToken)
                .ConfigureAwait(false);
            await EnsureFileAsync(Path.Combine(dir, "log.md"), SessionDefaults.EmptyLog, cancellationToken)
                .ConfigureAwait(false);
            await EnsureFileAsync(
                    Path.Combine(dir, "schema.md"),
                    string.IsNullOrWhiteSpace(appSchema) ? SessionDefaults.DefaultSchema : appSchema,
                    cancellationToken)
                .ConfigureAwait(false);
            await EnsureFileAsync(
                    Path.Combine(dir, "messages.json"),
                    JsonSerializer.Serialize(new SessionMessagesFile(), JsonOptions),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task AppendMessagesAsync(
        string appId,
        string userId,
        string sessionId,
        IEnumerable<OllamaMessage> messages,
        int maxMessages,
        CancellationToken cancellationToken = default)
    {
        var gate = GetLock(sessionId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var dir = GetSessionDir(sessionId);
            Directory.CreateDirectory(dir);
            await EnsureSessionAccessAsync(dir, appId, userId, cancellationToken).ConfigureAwait(false);

            var file = await LoadMessagesFileAsync(dir, cancellationToken).ConfigureAwait(false);
            file.Messages.AddRange(messages);
            file.Messages = Trim(file.Messages, maxMessages).ToList();
            await File.WriteAllTextAsync(
                    Path.Combine(dir, "messages.json"),
                    JsonSerializer.Serialize(file, JsonOptions),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task ApplyWikiUpdateAsync(
        string appId,
        string userId,
        string sessionId,
        SessionWikiUpdate update,
        CancellationToken cancellationToken = default)
    {
        var gate = GetLock(sessionId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var dir = GetSessionDir(sessionId);
            Directory.CreateDirectory(Path.Combine(dir, "pages"));
            await EnsureSessionAccessAsync(dir, appId, userId, cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(update.LogEntry))
            {
                var logPath = Path.Combine(dir, "log.md");
                var existing = File.Exists(logPath) ? await File.ReadAllTextAsync(logPath, cancellationToken).ConfigureAwait(false) : SessionDefaults.EmptyLog;
                await File.WriteAllTextAsync(logPath, existing.TrimEnd() + "\n\n" + update.LogEntry.Trim() + "\n", cancellationToken)
                    .ConfigureAwait(false);
            }

            foreach (var page in update.Pages)
            {
                if (!SessionWikiPagePaths.TryNormalize(page.Path, out var relative))
                    continue;

                var fullPath = Path.GetFullPath(Path.Combine(dir, relative));
                var pagesRoot = Path.GetFullPath(Path.Combine(dir, "pages"));
                if (!fullPath.StartsWith(pagesRoot, StringComparison.OrdinalIgnoreCase))
                    continue;

                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                await File.WriteAllTextAsync(fullPath, page.Content.Trim() + "\n", cancellationToken).ConfigureAwait(false);
            }

            SessionWikiIndexSync.HoistNestedPageFiles(dir);

            if (!string.IsNullOrWhiteSpace(update.IndexMd) || update.Pages.Count > 0)
            {
                var indexContent = SessionWikiIndexSync.Reconcile(dir, update.IndexMd);
                await File.WriteAllTextAsync(Path.Combine(dir, "index.md"), indexContent.Trim() + "\n", cancellationToken)
                    .ConfigureAwait(false);
            }

            foreach (var deletePage in update.DeletePages)
            {
                if (!SessionWikiPagePaths.TryNormalize(deletePage, out var relative))
                    continue;

                var fullPath = Path.GetFullPath(Path.Combine(dir, relative));
                var pagesRoot = Path.GetFullPath(Path.Combine(dir, "pages"));
                if (!fullPath.StartsWith(pagesRoot, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (File.Exists(fullPath))
                    File.Delete(fullPath);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<int> DeleteSessionsOlderThanAsync(
        string appId,
        DateTimeOffset olderThan,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_sessionsRoot))
            return 0;

        var deleted = 0;
        foreach (var dir in Directory.EnumerateDirectories(_sessionsRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var metaPath = Path.Combine(dir, "meta.json");
            if (!File.Exists(metaPath))
                continue;

            var meta = await LoadMetaAsync(metaPath, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(meta.AppId, appId, StringComparison.Ordinal))
                continue;

            var lastActivity = Directory.GetLastWriteTimeUtc(dir);
            if (lastActivity >= olderThan.UtcDateTime)
                continue;

            var sessionId = Path.GetFileName(dir);
            if (await TryDeleteSessionDirectoryAsync(sessionId, dir, cancellationToken).ConfigureAwait(false))
                deleted++;
        }

        return deleted;
    }

    public async Task<int> DeleteSessionsForUserAsync(
        string appId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_sessionsRoot))
            return 0;

        var deleted = 0;
        foreach (var dir in Directory.EnumerateDirectories(_sessionsRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var metaPath = Path.Combine(dir, "meta.json");
            if (!File.Exists(metaPath))
                continue;

            var meta = await LoadMetaAsync(metaPath, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(meta.AppId, appId, StringComparison.Ordinal)
                || !string.Equals(meta.UserId, userId, StringComparison.Ordinal))
                continue;

            var sessionId = Path.GetFileName(dir);
            if (await TryDeleteSessionDirectoryAsync(sessionId, dir, cancellationToken).ConfigureAwait(false))
                deleted++;
        }

        return deleted;
    }

    private async Task<bool> TryDeleteSessionDirectoryAsync(
        string sessionId,
        string dir,
        CancellationToken cancellationToken)
    {
        var gate = GetLock(sessionId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!Directory.Exists(dir))
                return false;

            Directory.Delete(dir, recursive: true);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        finally
        {
            gate.Release();
            _locks.TryRemove(SanitizeSessionId(sessionId), out _);
        }
    }

    private string GetSessionDir(string sessionId) =>
        Path.Combine(_sessionsRoot, SanitizeSessionId(sessionId));

    private SemaphoreSlim GetLock(string sessionId) =>
        _locks.GetOrAdd(SanitizeSessionId(sessionId), _ => new SemaphoreSlim(1, 1));

    private static string SanitizeSessionId(string sessionId)
    {
        var trimmed = sessionId.Trim();
        if (string.IsNullOrEmpty(trimmed))
            throw new ArgumentException("sessionId is required.");

        foreach (var c in Path.GetInvalidFileNameChars())
            trimmed = trimmed.Replace(c, '_');

        return trimmed;
    }

    private static async Task EnsureSessionAccessAsync(
        string dir,
        string appId,
        string userId,
        CancellationToken cancellationToken)
    {
        var metaPath = Path.Combine(dir, "meta.json");
        if (!File.Exists(metaPath))
            return;

        var meta = await LoadMetaAsync(metaPath, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(meta.AppId, appId, StringComparison.Ordinal)
            || !string.Equals(meta.UserId, userId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("This session belongs to another app or user.");
        }
    }

    private static async Task WriteMetaAsync(
        string dir,
        string appId,
        string userId,
        CancellationToken cancellationToken)
    {
        var metaPath = Path.Combine(dir, "meta.json");
        if (File.Exists(metaPath))
        {
            await EnsureSessionAccessAsync(dir, appId, userId, cancellationToken).ConfigureAwait(false);
            return;
        }

        var meta = new SessionMeta { AppId = appId, UserId = userId };
        await File.WriteAllTextAsync(metaPath, JsonSerializer.Serialize(meta, JsonOptions), cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<SessionMeta> LoadMetaAsync(string metaPath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(metaPath);
        return await JsonSerializer.DeserializeAsync<SessionMeta>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
               ?? new SessionMeta();
    }

    private static async Task<string> ReadOptionalAsync(string path, CancellationToken cancellationToken) =>
        File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false) : string.Empty;

    private static async Task EnsureFileAsync(string path, string content, CancellationToken cancellationToken)
    {
        if (File.Exists(path))
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<OllamaMessage>> LoadMessagesAsync(string dir, CancellationToken cancellationToken)
    {
        var file = await LoadMessagesFileAsync(dir, cancellationToken).ConfigureAwait(false);
        return file.Messages;
    }

    private static async Task<SessionMessagesFile> LoadMessagesFileAsync(string dir, CancellationToken cancellationToken)
    {
        var path = Path.Combine(dir, "messages.json");
        if (!File.Exists(path))
            return new SessionMessagesFile();

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<SessionMessagesFile>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
               ?? new SessionMessagesFile();
    }

    private static IReadOnlyList<OllamaMessage> Trim(List<OllamaMessage> messages, int maxMessages)
    {
        if (messages.Count <= maxMessages)
            return messages;
        return messages.Skip(messages.Count - maxMessages).ToList();
    }

    private sealed class SessionMessagesFile
    {
        public List<OllamaMessage> Messages { get; set; } = [];
    }

    private sealed class SessionMeta
    {
        public string AppId { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
    }
}
