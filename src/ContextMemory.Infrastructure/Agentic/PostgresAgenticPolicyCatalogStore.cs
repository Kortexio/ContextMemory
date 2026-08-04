using System.Text.Json;
using ContextMemory.Core.Agentic;
using ContextMemory.Core.Contracts;
using ContextMemory.Infrastructure.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ContextMemory.Infrastructure.Agentic;

public sealed class PostgresAgenticPolicyCatalogStore : IAgenticPolicyCatalogStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDbContextFactory<ContextMemoryDbContext> _dbFactory;
    private readonly ILogger<PostgresAgenticPolicyCatalogStore> _logger;
    private readonly SemaphoreSlim _seedLock = new(1, 1);
    private volatile bool _seeded;

    public PostgresAgenticPolicyCatalogStore(
        IDbContextFactory<ContextMemoryDbContext> dbFactory,
        ILogger<PostgresAgenticPolicyCatalogStore> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task EnsureSeededAsync(CancellationToken cancellationToken = default)
    {
        if (_seeded)
            return;

        await _seedLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_seeded)
                return;

            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var skillCount = await db.AgenticSkillCatalog.CountAsync(cancellationToken).ConfigureAwait(false);
            var guardrailCount = await db.AgenticGuardrailCatalog.CountAsync(cancellationToken).ConfigureAwait(false);

            if (skillCount == 0)
            {
                foreach (var skill in AgenticCatalogSeed.Skills)
                    db.AgenticSkillCatalog.Add(ToEntity(skill));
                _logger.LogInformation("Seeded {Count} agentic skills", AgenticCatalogSeed.Skills.Count);
            }

            if (guardrailCount == 0)
            {
                foreach (var g in AgenticCatalogSeed.Guardrails)
                    db.AgenticGuardrailCatalog.Add(ToEntity(g));
                _logger.LogInformation("Seeded {Count} agentic guardrails", AgenticCatalogSeed.Guardrails.Count);
            }

            if (skillCount == 0 || guardrailCount == 0)
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            _seeded = true;
        }
        finally
        {
            _seedLock.Release();
        }
    }

    public async Task<AgenticCatalogSnapshot> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSeededAsync(cancellationToken).ConfigureAwait(false);
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var skills = await db.AgenticSkillCatalog.AsNoTracking()
            .OrderBy(s => s.SortOrder).ThenBy(s => s.Name)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var guardrails = await db.AgenticGuardrailCatalog.AsNoTracking()
            .OrderBy(g => g.SortOrder).ThenBy(g => g.Name)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return new AgenticCatalogSnapshot
        {
            Skills = skills.Select(FromEntity).ToList(),
            Guardrails = guardrails.Select(FromEntity).ToList()
        };
    }

    public async Task<AgenticSkillDefinition?> GetSkillAsync(string id, CancellationToken cancellationToken = default)
    {
        await EnsureSeededAsync(cancellationToken).ConfigureAwait(false);
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var row = await db.AgenticSkillCatalog.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            .ConfigureAwait(false);
        return row is null ? null : FromEntity(row);
    }

    public async Task<AgenticSkillDefinition> UpsertSkillAsync(
        AgenticSkillDefinition skill,
        CancellationToken cancellationToken = default)
    {
        await EnsureSeededAsync(cancellationToken).ConfigureAwait(false);
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var row = await db.AgenticSkillCatalog.FirstOrDefaultAsync(s => s.Id == skill.Id, cancellationToken)
            .ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        if (row is null)
        {
            row = ToEntity(skill with
            {
                CreatedAt = now,
                UpdatedAt = now,
                IsSystem = false
            });
            db.AgenticSkillCatalog.Add(row);
        }
        else
        {
            if (row.IsSystem)
            {
                // System skills: allow editing prompt/metadata but keep IsSystem.
                row.Name = skill.Name;
                row.Description = skill.Description;
                row.PromptMarkdown = skill.PromptMarkdown;
                row.Category = skill.Category;
                row.IsDefaultEnabled = skill.IsDefaultEnabled;
                row.SortOrder = skill.SortOrder;
                row.LinkedGuardrailIdsJson = JsonSerializer.Serialize(skill.LinkedGuardrailIds, JsonOptions);
                row.UpdatedAt = now;
            }
            else
            {
                row.Name = skill.Name;
                row.Description = skill.Description;
                row.PromptMarkdown = skill.PromptMarkdown;
                row.Category = skill.Category;
                row.IsDefaultEnabled = skill.IsDefaultEnabled;
                row.SortOrder = skill.SortOrder;
                row.LinkedGuardrailIdsJson = JsonSerializer.Serialize(skill.LinkedGuardrailIds, JsonOptions);
                row.UpdatedAt = now;
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _seeded = true;
        return FromEntity(row);
    }

    public async Task<bool> DeleteSkillAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var row = await db.AgenticSkillCatalog.FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
            return false;
        if (row.IsSystem)
            throw new InvalidOperationException("System skills cannot be deleted.");

        db.AgenticSkillCatalog.Remove(row);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<IReadOnlyList<AgenticGuardrailDefinition>> ListGuardrailsAsync(
        CancellationToken cancellationToken = default)
    {
        var catalog = await GetCatalogAsync(cancellationToken).ConfigureAwait(false);
        return catalog.Guardrails;
    }

    public async Task<AgenticGuardrailDefinition?> GetGuardrailAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        await EnsureSeededAsync(cancellationToken).ConfigureAwait(false);
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var row = await db.AgenticGuardrailCatalog.AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == id, cancellationToken)
            .ConfigureAwait(false);
        return row is null ? null : FromEntity(row);
    }

    public async Task<AgenticGuardrailDefinition> UpsertGuardrailAsync(
        AgenticGuardrailDefinition guardrail,
        CancellationToken cancellationToken = default)
    {
        await EnsureSeededAsync(cancellationToken).ConfigureAwait(false);
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var row = await db.AgenticGuardrailCatalog
            .FirstOrDefaultAsync(g => g.Id == guardrail.Id, cancellationToken)
            .ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        if (row is null)
        {
            row = ToEntity(guardrail with
            {
                UpdatedAt = now,
                IsSystem = false,
                ConfigJson = string.IsNullOrWhiteSpace(guardrail.ConfigJson) ? "{}" : guardrail.ConfigJson
            });
            db.AgenticGuardrailCatalog.Add(row);
        }
        else
        {
            row.Name = guardrail.Name;
            row.Description = guardrail.Description;
            row.Kind = guardrail.Kind;
            row.ConfigJson = string.IsNullOrWhiteSpace(guardrail.ConfigJson) ? "{}" : guardrail.ConfigJson;
            row.IsDefaultEnabled = guardrail.IsDefaultEnabled;
            row.SortOrder = guardrail.SortOrder;
            row.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return FromEntity(row);
    }

    public async Task<bool> DeleteGuardrailAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var row = await db.AgenticGuardrailCatalog.FirstOrDefaultAsync(g => g.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
            return false;
        if (row.IsSystem)
            throw new InvalidOperationException("System guardrails cannot be deleted.");

        db.AgenticGuardrailCatalog.Remove(row);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static AgenticSkillCatalogEntity ToEntity(AgenticSkillDefinition skill) =>
        new()
        {
            Id = skill.Id,
            Name = skill.Name,
            Description = skill.Description,
            PromptMarkdown = skill.PromptMarkdown,
            Category = skill.Category,
            IsSystem = skill.IsSystem,
            IsDefaultEnabled = skill.IsDefaultEnabled,
            SortOrder = skill.SortOrder,
            LinkedGuardrailIdsJson = JsonSerializer.Serialize(skill.LinkedGuardrailIds, JsonOptions),
            CreatedAt = skill.CreatedAt == default ? DateTimeOffset.UtcNow : skill.CreatedAt,
            UpdatedAt = skill.UpdatedAt == default ? DateTimeOffset.UtcNow : skill.UpdatedAt
        };

    private static AgenticGuardrailCatalogEntity ToEntity(AgenticGuardrailDefinition g) =>
        new()
        {
            Id = g.Id,
            Name = g.Name,
            Description = g.Description,
            Kind = g.Kind,
            ConfigJson = string.IsNullOrWhiteSpace(g.ConfigJson) ? "{}" : g.ConfigJson,
            IsSystem = g.IsSystem,
            IsDefaultEnabled = g.IsDefaultEnabled,
            SortOrder = g.SortOrder,
            UpdatedAt = g.UpdatedAt == default ? DateTimeOffset.UtcNow : g.UpdatedAt
        };

    private static AgenticSkillDefinition FromEntity(AgenticSkillCatalogEntity row)
    {
        var linked = Array.Empty<string>();
        try
        {
            linked = JsonSerializer.Deserialize<string[]>(row.LinkedGuardrailIdsJson, JsonOptions) ?? [];
        }
        catch
        {
            // ignore
        }

        return new AgenticSkillDefinition
        {
            Id = row.Id,
            Name = row.Name,
            Description = row.Description,
            PromptMarkdown = row.PromptMarkdown,
            Category = row.Category,
            IsSystem = row.IsSystem,
            IsDefaultEnabled = row.IsDefaultEnabled,
            SortOrder = row.SortOrder,
            LinkedGuardrailIds = linked,
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt
        };
    }

    private static AgenticGuardrailDefinition FromEntity(AgenticGuardrailCatalogEntity row) =>
        new()
        {
            Id = row.Id,
            Name = row.Name,
            Description = row.Description,
            Kind = row.Kind,
            ConfigJson = row.ConfigJson,
            IsSystem = row.IsSystem,
            IsDefaultEnabled = row.IsDefaultEnabled,
            SortOrder = row.SortOrder,
            UpdatedAt = row.UpdatedAt
        };
}
