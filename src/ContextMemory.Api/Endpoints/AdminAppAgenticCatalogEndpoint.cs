using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ContextMemory.Core.Agentic;
using ContextMemory.Core.Contracts;

namespace ContextMemory.Api.Endpoints;

public static class AdminAppAgenticCatalogEndpoint
{
    public static void MapAdminAppAgenticCatalogEndpoints(this WebApplication app)
    {
        app.MapGet("/admin/apps/{appId}/agentic/catalog", GetCatalog);
        app.MapGet("/admin/apps/{appId}/skills/{id}", GetSkill);
        app.MapPost("/admin/apps/{appId}/skills", CreateSkill).DisableAntiforgery();
        app.MapPut("/admin/apps/{appId}/skills/{id}", UpdateSkill).DisableAntiforgery();
        app.MapDelete("/admin/apps/{appId}/skills/{id}", DeleteSkill);
        app.MapGet("/admin/apps/{appId}/skills/{id}/export", ExportSkill);
        app.MapPost("/admin/apps/{appId}/skills/import", ImportSkill).DisableAntiforgery();

        app.MapGet("/admin/apps/{appId}/guardrails/{id}", GetGuardrail);
        app.MapPost("/admin/apps/{appId}/guardrails", CreateGuardrail).DisableAntiforgery();
        app.MapPut("/admin/apps/{appId}/guardrails/{id}", UpdateGuardrail).DisableAntiforgery();
        app.MapDelete("/admin/apps/{appId}/guardrails/{id}", DeleteGuardrail);
        app.MapGet("/admin/apps/{appId}/guardrails/{id}/export", ExportGuardrail);
        app.MapPost("/admin/apps/{appId}/guardrails/import", ImportGuardrail).DisableAntiforgery();
    }

    private static async Task<IResult> GetCatalog(
        string appId,
        IAgenticAppPolicyCatalogStore catalog,
        CancellationToken cancellationToken)
    {
        var snapshot = await catalog.GetCatalogAsync(appId, cancellationToken).ConfigureAwait(false);
        return Results.Json(new { skills = snapshot.Skills, guardrails = snapshot.Guardrails });
    }

    private static async Task<IResult> GetSkill(
        string appId,
        string id,
        IAgenticAppPolicyCatalogStore catalog,
        CancellationToken cancellationToken)
    {
        var skill = await catalog.GetSkillAsync(appId, id, cancellationToken).ConfigureAwait(false);
        return skill is null ? Results.NotFound(new { error = "Skill not found." }) : Results.Json(skill);
    }

    private static async Task<IResult> CreateSkill(
        string appId,
        AgenticAppSkillUpsertRequest body,
        IAgenticAppPolicyCatalogStore catalog,
        CancellationToken cancellationToken)
    {
        var id = string.IsNullOrWhiteSpace(body.Id) ? Slugify(body.Name) : body.Id.Trim();
        if (string.IsNullOrWhiteSpace(id))
            return Results.BadRequest(new { error = "id or name is required." });

        var existing = await catalog.GetSkillAsync(appId, id, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
            return Results.Conflict(new { error = $"Skill '{id}' already exists for this app." });

        var created = await catalog.UpsertSkillAsync(ToSkill(appId, id, body), cancellationToken)
            .ConfigureAwait(false);
        return Results.Json(created);
    }

    private static async Task<IResult> UpdateSkill(
        string appId,
        string id,
        AgenticAppSkillUpsertRequest body,
        IAgenticAppPolicyCatalogStore catalog,
        CancellationToken cancellationToken)
    {
        var existing = await catalog.GetSkillAsync(appId, id, cancellationToken).ConfigureAwait(false);
        if (existing is null)
            return Results.NotFound(new { error = "Skill not found." });

        var updated = await catalog.UpsertSkillAsync(
                ToSkill(appId, id, body) with { CreatedAt = existing.CreatedAt },
                cancellationToken)
            .ConfigureAwait(false);
        return Results.Json(updated);
    }

    private static async Task<IResult> DeleteSkill(
        string appId,
        string id,
        IAgenticAppPolicyCatalogStore catalog,
        CancellationToken cancellationToken)
    {
        var deleted = await catalog.DeleteSkillAsync(appId, id, cancellationToken).ConfigureAwait(false);
        return deleted ? Results.NoContent() : Results.NotFound(new { error = "Skill not found." });
    }

    private static async Task<IResult> ExportSkill(
        string appId,
        string id,
        IAgenticAppPolicyCatalogStore catalog,
        CancellationToken cancellationToken)
    {
        var skill = await catalog.GetSkillAsync(appId, id, cancellationToken).ConfigureAwait(false);
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
            IsDefaultEnabled = skill.IsEnabled,
            SortOrder = skill.SortOrder
        };
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        return Results.File(Encoding.UTF8.GetBytes(json), "application/json", $"{skill.Id}.skill.json");
    }

    private static async Task<IResult> ImportSkill(
        string appId,
        HttpRequest request,
        IAgenticAppPolicyCatalogStore catalog,
        CancellationToken cancellationToken)
    {
        var replace = string.Equals(request.Query["replace"], "true", StringComparison.OrdinalIgnoreCase);
        AgenticSkillExportDto? dto = null;

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
            dto = ParseSkillImport(text);
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
        var existing = await catalog.GetSkillAsync(appId, id, cancellationToken).ConfigureAwait(false);
        if (existing is not null && !replace)
            return Results.Conflict(new { error = $"Skill '{id}' already exists. Pass replace=true to overwrite." });

        var upserted = await catalog.UpsertSkillAsync(
                new AgenticAppSkillDefinition
                {
                    AppId = appId,
                    Id = id,
                    Name = string.IsNullOrWhiteSpace(dto.Name) ? id : dto.Name,
                    Description = dto.Description ?? string.Empty,
                    Category = string.IsNullOrWhiteSpace(dto.Category) ? "general" : dto.Category,
                    PromptMarkdown = dto.PromptMarkdown ?? string.Empty,
                    LinkedGuardrailIds = dto.LinkedGuardrailIds ?? [],
                    IsEnabled = dto.IsDefaultEnabled,
                    SortOrder = dto.SortOrder,
                    CreatedAt = existing?.CreatedAt ?? DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                },
                cancellationToken)
            .ConfigureAwait(false);

        return Results.Json(upserted);
    }

    private static async Task<IResult> GetGuardrail(
        string appId,
        string id,
        IAgenticAppPolicyCatalogStore catalog,
        CancellationToken cancellationToken)
    {
        var guardrail = await catalog.GetGuardrailAsync(appId, id, cancellationToken).ConfigureAwait(false);
        return guardrail is null
            ? Results.NotFound(new { error = "Guardrail not found." })
            : Results.Json(guardrail);
    }

    private static async Task<IResult> CreateGuardrail(
        string appId,
        AgenticAppGuardrailUpsertRequest body,
        IAgenticAppPolicyCatalogStore catalog,
        CancellationToken cancellationToken)
    {
        var id = string.IsNullOrWhiteSpace(body.Id) ? Slugify(body.Name) : body.Id.Trim();
        if (string.IsNullOrWhiteSpace(id))
            return Results.BadRequest(new { error = "id or name is required." });
        if (string.IsNullOrWhiteSpace(body.Kind))
            return Results.BadRequest(new { error = "kind is required." });

        var existing = await catalog.GetGuardrailAsync(appId, id, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
            return Results.Conflict(new { error = $"Guardrail '{id}' already exists for this app." });

        var created = await catalog.UpsertGuardrailAsync(ToGuardrail(appId, id, body), cancellationToken)
            .ConfigureAwait(false);
        return Results.Json(created);
    }

    private static async Task<IResult> UpdateGuardrail(
        string appId,
        string id,
        AgenticAppGuardrailUpsertRequest body,
        IAgenticAppPolicyCatalogStore catalog,
        CancellationToken cancellationToken)
    {
        var existing = await catalog.GetGuardrailAsync(appId, id, cancellationToken).ConfigureAwait(false);
        if (existing is null)
            return Results.NotFound(new { error = "Guardrail not found." });
        if (string.IsNullOrWhiteSpace(body.Kind))
            return Results.BadRequest(new { error = "kind is required." });

        var updated = await catalog.UpsertGuardrailAsync(ToGuardrail(appId, id, body), cancellationToken)
            .ConfigureAwait(false);
        return Results.Json(updated);
    }

    private static async Task<IResult> DeleteGuardrail(
        string appId,
        string id,
        IAgenticAppPolicyCatalogStore catalog,
        CancellationToken cancellationToken)
    {
        var deleted = await catalog.DeleteGuardrailAsync(appId, id, cancellationToken).ConfigureAwait(false);
        return deleted ? Results.NoContent() : Results.NotFound(new { error = "Guardrail not found." });
    }

    private static async Task<IResult> ExportGuardrail(
        string appId,
        string id,
        IAgenticAppPolicyCatalogStore catalog,
        CancellationToken cancellationToken)
    {
        var guardrail = await catalog.GetGuardrailAsync(appId, id, cancellationToken).ConfigureAwait(false);
        if (guardrail is null)
            return Results.NotFound(new { error = "Guardrail not found." });

        var payload = new AgenticGuardrailExportDto
        {
            Id = guardrail.Id,
            Name = guardrail.Name,
            Description = guardrail.Description,
            Kind = guardrail.Kind,
            ConfigJson = guardrail.ConfigJson,
            IsDefaultEnabled = guardrail.IsEnabled,
            SortOrder = guardrail.SortOrder
        };
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        return Results.File(Encoding.UTF8.GetBytes(json), "application/json", $"{guardrail.Id}.guardrail.json");
    }

    private static async Task<IResult> ImportGuardrail(
        string appId,
        HttpRequest request,
        IAgenticAppPolicyCatalogStore catalog,
        CancellationToken cancellationToken)
    {
        var replace = string.Equals(request.Query["replace"], "true", StringComparison.OrdinalIgnoreCase);
        AgenticGuardrailExportDto? dto;

        if (request.HasFormContentType)
        {
            var form = await request.ReadFormAsync(cancellationToken).ConfigureAwait(false);
            replace = replace || string.Equals(form["replace"], "true", StringComparison.OrdinalIgnoreCase);
            var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
            if (file is null)
                return Results.BadRequest(new { error = "file is required." });

            await using var stream = file.OpenReadStream();
            dto = await JsonSerializer.DeserializeAsync<AgenticGuardrailExportDto>(
                    stream,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            dto = await JsonSerializer.DeserializeAsync<AgenticGuardrailExportDto>(
                    request.Body,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (dto is null || string.IsNullOrWhiteSpace(dto.Id) && string.IsNullOrWhiteSpace(dto.Name))
            return Results.BadRequest(new { error = "Invalid guardrail payload." });
        if (string.IsNullOrWhiteSpace(dto.Kind))
            return Results.BadRequest(new { error = "kind is required." });

        var id = string.IsNullOrWhiteSpace(dto.Id) ? Slugify(dto.Name) : dto.Id.Trim();
        var existing = await catalog.GetGuardrailAsync(appId, id, cancellationToken).ConfigureAwait(false);
        if (existing is not null && !replace)
            return Results.Conflict(new { error = $"Guardrail '{id}' already exists. Pass replace=true to overwrite." });

        var upserted = await catalog.UpsertGuardrailAsync(
                new AgenticAppGuardrailDefinition
                {
                    AppId = appId,
                    Id = id,
                    Name = string.IsNullOrWhiteSpace(dto.Name) ? id : dto.Name,
                    Description = dto.Description ?? string.Empty,
                    Kind = dto.Kind.Trim(),
                    ConfigJson = string.IsNullOrWhiteSpace(dto.ConfigJson) ? "{}" : dto.ConfigJson,
                    IsEnabled = dto.IsDefaultEnabled,
                    SortOrder = dto.SortOrder,
                    UpdatedAt = DateTimeOffset.UtcNow
                },
                cancellationToken)
            .ConfigureAwait(false);

        return Results.Json(upserted);
    }

    private static AgenticAppSkillDefinition ToSkill(string appId, string id, AgenticAppSkillUpsertRequest body) =>
        new()
        {
            AppId = appId,
            Id = id,
            Name = string.IsNullOrWhiteSpace(body.Name) ? id : body.Name.Trim(),
            Description = body.Description?.Trim() ?? string.Empty,
            PromptMarkdown = body.PromptMarkdown ?? string.Empty,
            Category = string.IsNullOrWhiteSpace(body.Category) ? "general" : body.Category.Trim(),
            IsEnabled = body.IsEnabled,
            SortOrder = body.SortOrder,
            LinkedGuardrailIds = body.LinkedGuardrailIds ?? [],
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

    private static AgenticAppGuardrailDefinition ToGuardrail(
        string appId,
        string id,
        AgenticAppGuardrailUpsertRequest body) =>
        new()
        {
            AppId = appId,
            Id = id,
            Name = string.IsNullOrWhiteSpace(body.Name) ? id : body.Name.Trim(),
            Description = body.Description?.Trim() ?? string.Empty,
            Kind = body.Kind.Trim(),
            ConfigJson = string.IsNullOrWhiteSpace(body.ConfigJson) ? "{}" : body.ConfigJson,
            IsEnabled = body.IsEnabled,
            SortOrder = body.SortOrder,
            UpdatedAt = DateTimeOffset.UtcNow
        };

    private static AgenticSkillExportDto? ParseSkillImport(string text)
    {
        text = text.Trim();
        if (text.StartsWith('{'))
            return JsonSerializer.Deserialize<AgenticSkillExportDto>(text, new JsonSerializerOptions(JsonSerializerDefaults.Web));

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
            IsDefaultEnabled = true,
            SortOrder = 500
        };
    }

    private static string Slugify(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;
        var slug = Regex.Replace(name.Trim().ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        return slug.Length > 128 ? slug[..128] : slug;
    }
}

public sealed class AgenticAppSkillUpsertRequest
{
    public string? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? PromptMarkdown { get; set; }
    public string? Category { get; set; }
    public bool IsEnabled { get; set; } = true;
    public int SortOrder { get; set; } = 500;
    public List<string>? LinkedGuardrailIds { get; set; }
}

public sealed class AgenticAppGuardrailUpsertRequest
{
    public string? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? ConfigJson { get; set; }
    public bool IsEnabled { get; set; } = true;
    public int SortOrder { get; set; } = 500;
}
