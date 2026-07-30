using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ContextMemory.Core.Agentic;
using ContextMemory.Core.Contracts;

namespace ContextMemory.Api.Endpoints;

public static class AdminAgenticCatalogEndpoint
{
    public static void MapAdminAgenticCatalogEndpoints(this WebApplication app)
    {
        app.MapGet("/admin/agentic/catalog", GetCatalog);
        app.MapGet("/admin/agentic/skills/{id}", GetSkill);
        app.MapPost("/admin/agentic/skills", CreateSkill).DisableAntiforgery();
        app.MapPut("/admin/agentic/skills/{id}", UpdateSkill).DisableAntiforgery();
        app.MapDelete("/admin/agentic/skills/{id}", DeleteSkill);
        app.MapGet("/admin/agentic/skills/{id}/export", ExportSkill);
        app.MapPost("/admin/agentic/skills/import", ImportSkill).DisableAntiforgery();
    }

    private static async Task<IResult> GetCatalog(
        IAgenticPolicyCatalogStore catalog,
        CancellationToken cancellationToken)
    {
        var snapshot = await catalog.GetCatalogAsync(cancellationToken).ConfigureAwait(false);
        return Results.Json(new
        {
            skills = snapshot.Skills,
            guardrails = snapshot.Guardrails
        });
    }

    private static async Task<IResult> GetSkill(
        string id,
        IAgenticPolicyCatalogStore catalog,
        CancellationToken cancellationToken)
    {
        var skill = await catalog.GetSkillAsync(id, cancellationToken).ConfigureAwait(false);
        return skill is null ? Results.NotFound(new { error = "Skill not found." }) : Results.Json(skill);
    }

    private static async Task<IResult> CreateSkill(
        AgenticSkillUpsertRequest body,
        IAgenticPolicyCatalogStore catalog,
        CancellationToken cancellationToken)
    {
        var id = string.IsNullOrWhiteSpace(body.Id) ? Slugify(body.Name) : body.Id.Trim();
        if (string.IsNullOrWhiteSpace(id))
            return Results.BadRequest(new { error = "id or name is required." });

        var existing = await catalog.GetSkillAsync(id, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
            return Results.Conflict(new { error = $"Skill '{id}' already exists." });

        var created = await catalog.UpsertSkillAsync(
                ToDefinition(id, body, isSystem: false),
                cancellationToken)
            .ConfigureAwait(false);
        return Results.Json(created);
    }

    private static async Task<IResult> UpdateSkill(
        string id,
        AgenticSkillUpsertRequest body,
        IAgenticPolicyCatalogStore catalog,
        CancellationToken cancellationToken)
    {
        var existing = await catalog.GetSkillAsync(id, cancellationToken).ConfigureAwait(false);
        if (existing is null)
            return Results.NotFound(new { error = "Skill not found." });

        var updated = await catalog.UpsertSkillAsync(
                ToDefinition(id, body, existing.IsSystem) with
                {
                    CreatedAt = existing.CreatedAt
                },
                cancellationToken)
            .ConfigureAwait(false);
        return Results.Json(updated);
    }

    private static async Task<IResult> DeleteSkill(
        string id,
        IAgenticPolicyCatalogStore catalog,
        CancellationToken cancellationToken)
    {
        try
        {
            var deleted = await catalog.DeleteSkillAsync(id, cancellationToken).ConfigureAwait(false);
            return deleted ? Results.NoContent() : Results.NotFound(new { error = "Skill not found." });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> ExportSkill(
        string id,
        IAgenticPolicyCatalogStore catalog,
        CancellationToken cancellationToken)
    {
        var skill = await catalog.GetSkillAsync(id, cancellationToken).ConfigureAwait(false);
        if (skill is null)
            return Results.NotFound(new { error = "Skill not found." });

        var payload = new AgenticSkillExportDto
        {
            Id = skill.Id,
            Name = skill.Name,
            Description = skill.Description,
            Category = skill.Category,
            PromptMarkdown = skill.PromptMarkdown,
            LinkedGuardrailIds = skill.LinkedGuardrailIds.ToList(),
            IsDefaultEnabled = skill.IsDefaultEnabled,
            SortOrder = skill.SortOrder
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        var bytes = Encoding.UTF8.GetBytes(json);
        return Results.File(bytes, "application/json", $"{skill.Id}.skill.json");
    }

    private static async Task<IResult> ImportSkill(
        HttpRequest request,
        IAgenticPolicyCatalogStore catalog,
        CancellationToken cancellationToken)
    {
        AgenticSkillExportDto? dto = null;
        var replace = string.Equals(request.Query["replace"], "true", StringComparison.OrdinalIgnoreCase);

        if (request.HasFormContentType)
        {
            var form = await request.ReadFormAsync(cancellationToken).ConfigureAwait(false);
            replace = replace || string.Equals(form["replace"], "true", StringComparison.OrdinalIgnoreCase);
            var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
            if (file is null)
                return Results.BadRequest(new { error = "file is required." });

            await using var stream = file.OpenReadStream();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var text = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            dto = ParseImport(text);
        }
        else
        {
            dto = await JsonSerializer.DeserializeAsync<AgenticSkillExportDto>(
                    request.Body,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (dto is null || string.IsNullOrWhiteSpace(dto.Id) && string.IsNullOrWhiteSpace(dto.Name))
            return Results.BadRequest(new { error = "Invalid skill payload." });

        var id = string.IsNullOrWhiteSpace(dto.Id) ? Slugify(dto.Name) : dto.Id.Trim();
        var existing = await catalog.GetSkillAsync(id, cancellationToken).ConfigureAwait(false);
        if (existing is not null && !replace)
            return Results.Conflict(new { error = $"Skill '{id}' already exists. Pass replace=true to overwrite." });

        var upserted = await catalog.UpsertSkillAsync(
                new AgenticSkillDefinition
                {
                    Id = id,
                    Name = string.IsNullOrWhiteSpace(dto.Name) ? id : dto.Name,
                    Description = dto.Description ?? string.Empty,
                    Category = string.IsNullOrWhiteSpace(dto.Category) ? "general" : dto.Category,
                    PromptMarkdown = dto.PromptMarkdown ?? string.Empty,
                    LinkedGuardrailIds = dto.LinkedGuardrailIds ?? [],
                    IsDefaultEnabled = dto.IsDefaultEnabled,
                    SortOrder = dto.SortOrder,
                    IsSystem = existing?.IsSystem ?? false,
                    CreatedAt = existing?.CreatedAt ?? DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                },
                cancellationToken)
            .ConfigureAwait(false);

        return Results.Json(upserted);
    }

    private static AgenticSkillExportDto? ParseImport(string text)
    {
        text = text.Trim();
        if (text.StartsWith('{'))
            return JsonSerializer.Deserialize<AgenticSkillExportDto>(text, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        // Markdown with optional YAML-like frontmatter between ---
        string? id = null, name = null, description = null, category = null;
        var markdown = text;
        if (text.StartsWith("---"))
        {
            var end = text.IndexOf("---", 3, StringComparison.Ordinal);
            if (end > 0)
            {
                var header = text[3..end];
                markdown = text[(end + 3)..].Trim();
                foreach (var line in header.Split('\n'))
                {
                    var idx = line.IndexOf(':');
                    if (idx <= 0) continue;
                    var key = line[..idx].Trim().ToLowerInvariant();
                    var val = line[(idx + 1)..].Trim().Trim('"');
                    switch (key)
                    {
                        case "id": id = val; break;
                        case "name": name = val; break;
                        case "description": description = val; break;
                        case "category": category = val; break;
                    }
                }
            }
        }

        return new AgenticSkillExportDto
        {
            Id = id ?? Slugify(name ?? "imported-skill"),
            Name = name ?? id ?? "Imported skill",
            Description = description ?? string.Empty,
            Category = category ?? "general",
            PromptMarkdown = markdown,
            IsDefaultEnabled = false,
            SortOrder = 500
        };
    }

    private static AgenticSkillDefinition ToDefinition(string id, AgenticSkillUpsertRequest body, bool isSystem) =>
        new()
        {
            Id = id,
            Name = string.IsNullOrWhiteSpace(body.Name) ? id : body.Name.Trim(),
            Description = body.Description?.Trim() ?? string.Empty,
            PromptMarkdown = body.PromptMarkdown ?? string.Empty,
            Category = string.IsNullOrWhiteSpace(body.Category) ? "general" : body.Category.Trim(),
            IsSystem = isSystem,
            IsDefaultEnabled = body.IsDefaultEnabled,
            SortOrder = body.SortOrder,
            LinkedGuardrailIds = body.LinkedGuardrailIds ?? [],
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

    private static string Slugify(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;
        var slug = Regex.Replace(name.Trim().ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        return slug.Length > 128 ? slug[..128] : slug;
    }
}

public sealed class AgenticSkillUpsertRequest
{
    public string? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? PromptMarkdown { get; set; }
    public string? Category { get; set; }
    public bool IsDefaultEnabled { get; set; }
    public int SortOrder { get; set; } = 500;
    public List<string>? LinkedGuardrailIds { get; set; }
}

public sealed class AgenticSkillExportDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Category { get; set; }
    public string? PromptMarkdown { get; set; }
    public List<string>? LinkedGuardrailIds { get; set; }
    public bool IsDefaultEnabled { get; set; }
    public int SortOrder { get; set; }
}
