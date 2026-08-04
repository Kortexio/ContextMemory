using System.Text.Json;
using ContextMemory.Core.Agentic;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using Microsoft.Extensions.Options;

namespace ContextMemory.Infrastructure.Agentic;

public sealed class FileAgenticAppPolicyCatalogStore : IAgenticAppPolicyCatalogStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string _root;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public FileAgenticAppPolicyCatalogStore(IOptions<ContextMemoryOptions> options)
    {
        var cfg = options.Value;
        _root = Path.Combine(Path.GetFullPath(cfg.DataPath, cfg.ContentRootPath), "agentic-catalog", "apps");
        Directory.CreateDirectory(_root);
    }

    public async Task<AgenticAppCatalogSnapshot> GetCatalogAsync(
        string appId,
        CancellationToken cancellationToken = default)
    {
        appId = NormalizeAppId(appId);
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureAppDir(appId);
            var skills = await ReadSkillsAsync(appId, cancellationToken).ConfigureAwait(false);
            var guardrails = await ReadGuardrailsAsync(appId, cancellationToken).ConfigureAwait(false);
            return new AgenticAppCatalogSnapshot
            {
                Skills = skills.OrderBy(s => s.SortOrder).ThenBy(s => s.Name).ToList(),
                Guardrails = guardrails.OrderBy(g => g.SortOrder).ThenBy(g => g.Name).ToList()
            };
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<AgenticAppSkillDefinition?> GetSkillAsync(
        string appId,
        string id,
        CancellationToken cancellationToken = default)
    {
        var catalog = await GetCatalogAsync(appId, cancellationToken).ConfigureAwait(false);
        return catalog.Skills.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<AgenticAppSkillDefinition> UpsertSkillAsync(
        AgenticAppSkillDefinition skill,
        CancellationToken cancellationToken = default)
    {
        var appId = NormalizeAppId(skill.AppId);
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureAppDir(appId);
            var skills = await ReadSkillsAsync(appId, cancellationToken).ConfigureAwait(false);
            var idx = skills.FindIndex(s => string.Equals(s.Id, skill.Id, StringComparison.OrdinalIgnoreCase));
            var now = DateTimeOffset.UtcNow;
            var next = skill with
            {
                AppId = appId,
                Id = skill.Id.Trim(),
                Category = string.IsNullOrWhiteSpace(skill.Category) ? "general" : skill.Category,
                CreatedAt = idx < 0 ? now : skills[idx].CreatedAt,
                UpdatedAt = now
            };

            if (idx < 0)
                skills.Add(next);
            else
                skills[idx] = next;

            await WriteSkillsAsync(appId, skills, cancellationToken).ConfigureAwait(false);
            return next;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> DeleteSkillAsync(
        string appId,
        string id,
        CancellationToken cancellationToken = default)
    {
        appId = NormalizeAppId(appId);
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureAppDir(appId);
            var skills = await ReadSkillsAsync(appId, cancellationToken).ConfigureAwait(false);
            var n = skills.RemoveAll(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
            if (n == 0)
                return false;
            await WriteSkillsAsync(appId, skills, cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<AgenticAppGuardrailDefinition?> GetGuardrailAsync(
        string appId,
        string id,
        CancellationToken cancellationToken = default)
    {
        var catalog = await GetCatalogAsync(appId, cancellationToken).ConfigureAwait(false);
        return catalog.Guardrails.FirstOrDefault(g => string.Equals(g.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<AgenticAppGuardrailDefinition> UpsertGuardrailAsync(
        AgenticAppGuardrailDefinition guardrail,
        CancellationToken cancellationToken = default)
    {
        var appId = NormalizeAppId(guardrail.AppId);
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureAppDir(appId);
            var list = await ReadGuardrailsAsync(appId, cancellationToken).ConfigureAwait(false);
            var idx = list.FindIndex(g => string.Equals(g.Id, guardrail.Id, StringComparison.OrdinalIgnoreCase));
            var now = DateTimeOffset.UtcNow;
            var next = guardrail with
            {
                AppId = appId,
                Id = guardrail.Id.Trim(),
                ConfigJson = string.IsNullOrWhiteSpace(guardrail.ConfigJson) ? "{}" : guardrail.ConfigJson,
                UpdatedAt = now
            };

            if (idx < 0)
                list.Add(next);
            else
                list[idx] = next;

            await WriteGuardrailsAsync(appId, list, cancellationToken).ConfigureAwait(false);
            return next;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> DeleteGuardrailAsync(
        string appId,
        string id,
        CancellationToken cancellationToken = default)
    {
        appId = NormalizeAppId(appId);
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureAppDir(appId);
            var list = await ReadGuardrailsAsync(appId, cancellationToken).ConfigureAwait(false);
            var n = list.RemoveAll(g => string.Equals(g.Id, id, StringComparison.OrdinalIgnoreCase));
            if (n == 0)
                return false;
            await WriteGuardrailsAsync(appId, list, cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    private void EnsureAppDir(string appId) =>
        Directory.CreateDirectory(Path.Combine(_root, Sanitize(appId)));

    private string SkillsPath(string appId) => Path.Combine(_root, Sanitize(appId), "skills.json");
    private string GuardrailsPath(string appId) => Path.Combine(_root, Sanitize(appId), "guardrails.json");

    private async Task<List<AgenticAppSkillDefinition>> ReadSkillsAsync(string appId, CancellationToken ct)
    {
        var path = SkillsPath(appId);
        if (!File.Exists(path))
            return [];
        var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize<List<AgenticAppSkillDefinition>>(json, JsonOptions) ?? [];
    }

    private async Task<List<AgenticAppGuardrailDefinition>> ReadGuardrailsAsync(string appId, CancellationToken ct)
    {
        var path = GuardrailsPath(appId);
        if (!File.Exists(path))
            return [];
        var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize<List<AgenticAppGuardrailDefinition>>(json, JsonOptions) ?? [];
    }

    private Task WriteSkillsAsync(string appId, List<AgenticAppSkillDefinition> skills, CancellationToken ct) =>
        File.WriteAllTextAsync(SkillsPath(appId), JsonSerializer.Serialize(skills, JsonOptions), ct);

    private Task WriteGuardrailsAsync(string appId, List<AgenticAppGuardrailDefinition> guardrails, CancellationToken ct) =>
        File.WriteAllTextAsync(GuardrailsPath(appId), JsonSerializer.Serialize(guardrails, JsonOptions), ct);

    private static string NormalizeAppId(string appId) =>
        string.IsNullOrWhiteSpace(appId)
            ? throw new ArgumentException("appId is required.", nameof(appId))
            : appId.Trim();

    private static string Sanitize(string appId)
    {
        var chars = appId.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray();
        return new string(chars);
    }
}
