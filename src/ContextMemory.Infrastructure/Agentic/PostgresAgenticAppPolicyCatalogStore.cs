using System.Text.Json;
using ContextMemory.Core.Agentic;
using ContextMemory.Core.Contracts;
using ContextMemory.Infrastructure.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;

namespace ContextMemory.Infrastructure.Agentic;

public sealed class PostgresAgenticAppPolicyCatalogStore : IAgenticAppPolicyCatalogStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IDbContextFactory<ContextMemoryDbContext> _dbFactory;

    public PostgresAgenticAppPolicyCatalogStore(IDbContextFactory<ContextMemoryDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<AgenticAppCatalogSnapshot> GetCatalogAsync(
        string appId,
        CancellationToken cancellationToken = default)
    {
        appId = NormalizeAppId(appId);
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var skills = await db.AgenticAppSkills.AsNoTracking()
            .Where(s => s.AppId == appId)
            .OrderBy(s => s.SortOrder).ThenBy(s => s.Name)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var guardrails = await db.AgenticAppGuardrails.AsNoTracking()
            .Where(g => g.AppId == appId)
            .OrderBy(g => g.SortOrder).ThenBy(g => g.Name)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return new AgenticAppCatalogSnapshot
        {
            Skills = skills.Select(FromSkill).ToList(),
            Guardrails = guardrails.Select(FromGuardrail).ToList()
        };
    }

    public async Task<AgenticAppSkillDefinition?> GetSkillAsync(
        string appId,
        string id,
        CancellationToken cancellationToken = default)
    {
        appId = NormalizeAppId(appId);
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var row = await db.AgenticAppSkills.AsNoTracking()
            .FirstOrDefaultAsync(s => s.AppId == appId && s.Id == id, cancellationToken)
            .ConfigureAwait(false);
        return row is null ? null : FromSkill(row);
    }

    public async Task<AgenticAppSkillDefinition> UpsertSkillAsync(
        AgenticAppSkillDefinition skill,
        CancellationToken cancellationToken = default)
    {
        var appId = NormalizeAppId(skill.AppId);
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var row = await db.AgenticAppSkills
            .FirstOrDefaultAsync(s => s.AppId == appId && s.Id == skill.Id, cancellationToken)
            .ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        if (row is null)
        {
            row = new AgenticAppSkillEntity
            {
                AppId = appId,
                Id = skill.Id.Trim(),
                Name = skill.Name,
                Description = skill.Description,
                PromptMarkdown = skill.PromptMarkdown,
                Category = string.IsNullOrWhiteSpace(skill.Category) ? "general" : skill.Category,
                IsEnabled = skill.IsEnabled,
                SortOrder = skill.SortOrder,
                LinkedGuardrailIdsJson = JsonSerializer.Serialize(skill.LinkedGuardrailIds, JsonOptions),
                CreatedAt = now,
                UpdatedAt = now
            };
            db.AgenticAppSkills.Add(row);
        }
        else
        {
            row.Name = skill.Name;
            row.Description = skill.Description;
            row.PromptMarkdown = skill.PromptMarkdown;
            row.Category = string.IsNullOrWhiteSpace(skill.Category) ? "general" : skill.Category;
            row.IsEnabled = skill.IsEnabled;
            row.SortOrder = skill.SortOrder;
            row.LinkedGuardrailIdsJson = JsonSerializer.Serialize(skill.LinkedGuardrailIds, JsonOptions);
            row.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return FromSkill(row);
    }

    public async Task<bool> DeleteSkillAsync(
        string appId,
        string id,
        CancellationToken cancellationToken = default)
    {
        appId = NormalizeAppId(appId);
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var row = await db.AgenticAppSkills
            .FirstOrDefaultAsync(s => s.AppId == appId && s.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
            return false;

        db.AgenticAppSkills.Remove(row);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<AgenticAppGuardrailDefinition?> GetGuardrailAsync(
        string appId,
        string id,
        CancellationToken cancellationToken = default)
    {
        appId = NormalizeAppId(appId);
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var row = await db.AgenticAppGuardrails.AsNoTracking()
            .FirstOrDefaultAsync(g => g.AppId == appId && g.Id == id, cancellationToken)
            .ConfigureAwait(false);
        return row is null ? null : FromGuardrail(row);
    }

    public async Task<AgenticAppGuardrailDefinition> UpsertGuardrailAsync(
        AgenticAppGuardrailDefinition guardrail,
        CancellationToken cancellationToken = default)
    {
        var appId = NormalizeAppId(guardrail.AppId);
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var row = await db.AgenticAppGuardrails
            .FirstOrDefaultAsync(g => g.AppId == appId && g.Id == guardrail.Id, cancellationToken)
            .ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        if (row is null)
        {
            row = new AgenticAppGuardrailEntity
            {
                AppId = appId,
                Id = guardrail.Id.Trim(),
                Name = guardrail.Name,
                Description = guardrail.Description,
                Kind = guardrail.Kind,
                ConfigJson = string.IsNullOrWhiteSpace(guardrail.ConfigJson) ? "{}" : guardrail.ConfigJson,
                IsEnabled = guardrail.IsEnabled,
                SortOrder = guardrail.SortOrder,
                UpdatedAt = now
            };
            db.AgenticAppGuardrails.Add(row);
        }
        else
        {
            row.Name = guardrail.Name;
            row.Description = guardrail.Description;
            row.Kind = guardrail.Kind;
            row.ConfigJson = string.IsNullOrWhiteSpace(guardrail.ConfigJson) ? "{}" : guardrail.ConfigJson;
            row.IsEnabled = guardrail.IsEnabled;
            row.SortOrder = guardrail.SortOrder;
            row.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return FromGuardrail(row);
    }

    public async Task<bool> DeleteGuardrailAsync(
        string appId,
        string id,
        CancellationToken cancellationToken = default)
    {
        appId = NormalizeAppId(appId);
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var row = await db.AgenticAppGuardrails
            .FirstOrDefaultAsync(g => g.AppId == appId && g.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
            return false;

        db.AgenticAppGuardrails.Remove(row);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static string NormalizeAppId(string appId) =>
        string.IsNullOrWhiteSpace(appId)
            ? throw new ArgumentException("appId is required.", nameof(appId))
            : appId.Trim();

    private static AgenticAppSkillDefinition FromSkill(AgenticAppSkillEntity row)
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

        return new AgenticAppSkillDefinition
        {
            AppId = row.AppId,
            Id = row.Id,
            Name = row.Name,
            Description = row.Description,
            PromptMarkdown = row.PromptMarkdown,
            Category = row.Category,
            IsEnabled = row.IsEnabled,
            SortOrder = row.SortOrder,
            LinkedGuardrailIds = linked,
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt
        };
    }

    private static AgenticAppGuardrailDefinition FromGuardrail(AgenticAppGuardrailEntity row) =>
        new()
        {
            AppId = row.AppId,
            Id = row.Id,
            Name = row.Name,
            Description = row.Description,
            Kind = row.Kind,
            ConfigJson = row.ConfigJson,
            IsEnabled = row.IsEnabled,
            SortOrder = row.SortOrder,
            UpdatedAt = row.UpdatedAt
        };
}
