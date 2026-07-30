using System.Text.Json;
using ContextMemory.Core.Agentic;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ContextMemory.Infrastructure.Agentic;

public sealed class FileAgenticPolicyCatalogStore : IAgenticPolicyCatalogStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string _root;
    private readonly string _skillsPath;
    private readonly string _guardrailsPath;
    private readonly ILogger<FileAgenticPolicyCatalogStore> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public FileAgenticPolicyCatalogStore(
        IOptions<ContextMemoryOptions> options,
        ILogger<FileAgenticPolicyCatalogStore> logger)
    {
        var cfg = options.Value;
        _root = Path.Combine(Path.GetFullPath(cfg.DataPath, cfg.ContentRootPath), "agentic-catalog");
        Directory.CreateDirectory(_root);
        _skillsPath = Path.Combine(_root, "skills.json");
        _guardrailsPath = Path.Combine(_root, "guardrails.json");
        _logger = logger;
    }

    public async Task EnsureSeededAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_skillsPath))
            {
                await WriteSkillsAsync(AgenticCatalogSeed.Skills.ToList(), cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Seeded file agentic skills catalog");
            }

            if (!File.Exists(_guardrailsPath))
            {
                await WriteGuardrailsAsync(AgenticCatalogSeed.Guardrails.ToList(), cancellationToken)
                    .ConfigureAwait(false);
                _logger.LogInformation("Seeded file agentic guardrails catalog");
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<AgenticCatalogSnapshot> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSeededAsync(cancellationToken).ConfigureAwait(false);
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var skills = await ReadSkillsAsync(cancellationToken).ConfigureAwait(false);
            var guardrails = await ReadGuardrailsAsync(cancellationToken).ConfigureAwait(false);
            return new AgenticCatalogSnapshot
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

    public async Task<AgenticSkillDefinition?> GetSkillAsync(string id, CancellationToken cancellationToken = default)
    {
        var catalog = await GetCatalogAsync(cancellationToken).ConfigureAwait(false);
        return catalog.Skills.FirstOrDefault(s =>
            string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<AgenticSkillDefinition> UpsertSkillAsync(
        AgenticSkillDefinition skill,
        CancellationToken cancellationToken = default)
    {
        await EnsureSeededAsync(cancellationToken).ConfigureAwait(false);
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var skills = await ReadSkillsAsync(cancellationToken).ConfigureAwait(false);
            var idx = skills.FindIndex(s => string.Equals(s.Id, skill.Id, StringComparison.OrdinalIgnoreCase));
            var now = DateTimeOffset.UtcNow;

            if (idx < 0)
            {
                skills.Add(skill with
                {
                    IsSystem = false,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }
            else
            {
                var existing = skills[idx];
                skills[idx] = existing with
                {
                    Name = skill.Name,
                    Description = skill.Description,
                    PromptMarkdown = skill.PromptMarkdown,
                    Category = skill.Category,
                    IsDefaultEnabled = skill.IsDefaultEnabled,
                    SortOrder = skill.SortOrder,
                    LinkedGuardrailIds = skill.LinkedGuardrailIds,
                    UpdatedAt = now
                };
            }

            await WriteSkillsAsync(skills, cancellationToken).ConfigureAwait(false);
            return skills.First(s => string.Equals(s.Id, skill.Id, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> DeleteSkillAsync(string id, CancellationToken cancellationToken = default)
    {
        await EnsureSeededAsync(cancellationToken).ConfigureAwait(false);
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var skills = await ReadSkillsAsync(cancellationToken).ConfigureAwait(false);
            var existing = skills.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
                return false;
            if (existing.IsSystem)
                throw new InvalidOperationException("System skills cannot be deleted.");

            skills.RemoveAll(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
            await WriteSkillsAsync(skills, cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<AgenticGuardrailDefinition>> ListGuardrailsAsync(
        CancellationToken cancellationToken = default)
    {
        var catalog = await GetCatalogAsync(cancellationToken).ConfigureAwait(false);
        return catalog.Guardrails;
    }

    private async Task<List<AgenticSkillDefinition>> ReadSkillsAsync(CancellationToken ct)
    {
        var json = await File.ReadAllTextAsync(_skillsPath, ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize<List<AgenticSkillDefinition>>(json, JsonOptions) ?? [];
    }

    private async Task<List<AgenticGuardrailDefinition>> ReadGuardrailsAsync(CancellationToken ct)
    {
        var json = await File.ReadAllTextAsync(_guardrailsPath, ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize<List<AgenticGuardrailDefinition>>(json, JsonOptions) ?? [];
    }

    private Task WriteSkillsAsync(List<AgenticSkillDefinition> skills, CancellationToken ct) =>
        File.WriteAllTextAsync(_skillsPath, JsonSerializer.Serialize(skills, JsonOptions), ct);

    private Task WriteGuardrailsAsync(List<AgenticGuardrailDefinition> guardrails, CancellationToken ct) =>
        File.WriteAllTextAsync(_guardrailsPath, JsonSerializer.Serialize(guardrails, JsonOptions), ct);
}
