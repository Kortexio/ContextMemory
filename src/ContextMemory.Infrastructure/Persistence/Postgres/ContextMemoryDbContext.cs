using Microsoft.EntityFrameworkCore;

namespace ContextMemory.Infrastructure.Persistence.Postgres;

public sealed class ContextMemoryDbContext : DbContext
{
    public ContextMemoryDbContext(DbContextOptions<ContextMemoryDbContext> options)
        : base(options)
    {
    }

    public DbSet<RegisteredAppEntity> RegisteredApps => Set<RegisteredAppEntity>();
    public DbSet<AppProfileEntity> AppProfiles => Set<AppProfileEntity>();
    public DbSet<SessionRecordEntity> SessionRecords => Set<SessionRecordEntity>();
    public DbSet<AgenticPendingRecordEntity> AgenticPendingRecords => Set<AgenticPendingRecordEntity>();
    public DbSet<GlobalWikiDocumentEntity> GlobalWikiDocuments => Set<GlobalWikiDocumentEntity>();
    public DbSet<McpCredentialEntity> McpCredentials => Set<McpCredentialEntity>();
    public DbSet<McpCatalogToolEntity> McpCatalogTools => Set<McpCatalogToolEntity>();
    public DbSet<McpCatalogSyncEntity> McpCatalogSync => Set<McpCatalogSyncEntity>();
    public DbSet<AgenticSkillCatalogEntity> AgenticSkillCatalog => Set<AgenticSkillCatalogEntity>();
    public DbSet<AgenticGuardrailCatalogEntity> AgenticGuardrailCatalog => Set<AgenticGuardrailCatalogEntity>();
    public DbSet<AgenticAppSkillEntity> AgenticAppSkills => Set<AgenticAppSkillEntity>();
    public DbSet<AgenticAppGuardrailEntity> AgenticAppGuardrails => Set<AgenticAppGuardrailEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RegisteredAppEntity>(e =>
        {
            e.ToTable("registered_apps");
            e.HasKey(x => x.AppId);
            e.Property(x => x.AppId).HasMaxLength(64);
        });

        modelBuilder.Entity<AppProfileEntity>(e =>
        {
            e.ToTable("app_profiles");
            e.HasKey(x => x.AppId);
            e.Property(x => x.AppId).HasMaxLength(64);
            e.Property(x => x.ConfigJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<SessionRecordEntity>(e =>
        {
            e.ToTable("session_records");
            e.HasKey(x => new { x.AppId, x.UserId, x.SessionId });
            e.Property(x => x.AppId).HasMaxLength(64);
            e.Property(x => x.UserId).HasMaxLength(128);
            e.Property(x => x.SessionId).HasMaxLength(128);
            e.Property(x => x.DataJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<AgenticPendingRecordEntity>(e =>
        {
            e.ToTable("agentic_pending_records");
            e.HasKey(x => new { x.AppId, x.UserId, x.SessionId });
            e.Property(x => x.AppId).HasMaxLength(64);
            e.Property(x => x.UserId).HasMaxLength(128);
            e.Property(x => x.SessionId).HasMaxLength(128);
            e.Property(x => x.StateJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<GlobalWikiDocumentEntity>(e =>
        {
            e.ToTable("global_wiki_documents");
            e.HasKey(x => new { x.AppId, x.DocumentId, x.RevisionId });
            e.Property(x => x.AppId).HasMaxLength(64);
            e.Property(x => x.DocumentId).HasMaxLength(256);
            e.Property(x => x.RevisionId).HasMaxLength(64);
            e.Property(x => x.Slug).HasMaxLength(128);
            e.Property(x => x.Title).HasMaxLength(512);
            e.Property(x => x.Content).HasColumnType("text");
            e.Property(x => x.Summary).HasColumnType("text");
            e.Property(x => x.SourceId).HasMaxLength(128);
            e.Property(x => x.MetadataJson).HasColumnType("jsonb");
            e.Property(x => x.ContentHash).HasMaxLength(64);
            e.Property(x => x.Status).HasMaxLength(32);
            e.Property(x => x.SupersedesRevisionId).HasMaxLength(64);
            e.HasIndex(x => x.AppId);
            e.HasIndex(x => new { x.AppId, x.UpdatedAt });
            e.HasIndex(x => new { x.AppId, x.SourceId });
            e.HasIndex(x => new { x.AppId, x.DocumentId, x.Status });
            e.HasIndex(x => new { x.AppId, x.ValidFrom, x.ValidTo });
        });

        modelBuilder.Entity<McpCredentialEntity>(e =>
        {
            e.ToTable("mcp_credentials");
            e.HasKey(x => new { x.AppId, x.IntegrationName, x.CredentialRef });
            e.Property(x => x.AppId).HasMaxLength(64);
            e.Property(x => x.IntegrationName).HasMaxLength(128);
            e.Property(x => x.CredentialRef).HasMaxLength(128);
            e.Property(x => x.AuthMode).HasMaxLength(64);
            e.Property(x => x.SecretJson).HasColumnType("jsonb");
            e.HasIndex(x => x.AppId);
        });

        modelBuilder.Entity<McpCatalogToolEntity>(e =>
        {
            e.ToTable("mcp_catalog_tools");
            e.HasKey(x => new { x.AppId, x.IntegrationName, x.QualifiedName });
            e.Property(x => x.AppId).HasMaxLength(64);
            e.Property(x => x.IntegrationName).HasMaxLength(128);
            e.Property(x => x.QualifiedName).HasMaxLength(256);
            e.Property(x => x.ToolName).HasMaxLength(128);
            e.Property(x => x.Description).HasColumnType("text");
            e.Property(x => x.InputSchemaJson).HasColumnType("jsonb");
            e.Property(x => x.CapabilitiesJson).HasColumnType("jsonb");
            e.HasIndex(x => x.AppId);
            e.HasIndex(x => new { x.AppId, x.IntegrationName });
        });

        modelBuilder.Entity<McpCatalogSyncEntity>(e =>
        {
            e.ToTable("mcp_catalog_sync");
            e.HasKey(x => new { x.AppId, x.IntegrationName });
            e.Property(x => x.AppId).HasMaxLength(64);
            e.Property(x => x.IntegrationName).HasMaxLength(128);
            e.Property(x => x.LastError).HasColumnType("text");
            e.Property(x => x.SyncStatus).HasMaxLength(32);
            e.HasIndex(x => x.AppId);
        });

        modelBuilder.Entity<AgenticSkillCatalogEntity>(e =>
        {
            e.ToTable("agentic_skill_catalog");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(128);
            e.Property(x => x.Name).HasMaxLength(256);
            e.Property(x => x.Description).HasColumnType("text");
            e.Property(x => x.PromptMarkdown).HasColumnType("text");
            e.Property(x => x.Category).HasMaxLength(64);
            e.Property(x => x.Activation).HasMaxLength(32);
            e.Property(x => x.LinkedGuardrailIdsJson).HasColumnType("jsonb");
            e.HasIndex(x => x.SortOrder);
        });

        modelBuilder.Entity<AgenticGuardrailCatalogEntity>(e =>
        {
            e.ToTable("agentic_guardrail_catalog");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(128);
            e.Property(x => x.Name).HasMaxLength(256);
            e.Property(x => x.Description).HasColumnType("text");
            e.Property(x => x.Kind).HasMaxLength(64);
            e.Property(x => x.ConfigJson).HasColumnType("jsonb");
            e.HasIndex(x => x.Kind);
            e.HasIndex(x => x.SortOrder);
        });

        modelBuilder.Entity<AgenticAppSkillEntity>(e =>
        {
            e.ToTable("agentic_app_skills");
            e.HasKey(x => new { x.AppId, x.Id });
            e.Property(x => x.AppId).HasMaxLength(64);
            e.Property(x => x.Id).HasMaxLength(128);
            e.Property(x => x.Name).HasMaxLength(256);
            e.Property(x => x.Description).HasColumnType("text");
            e.Property(x => x.PromptMarkdown).HasColumnType("text");
            e.Property(x => x.Category).HasMaxLength(64);
            e.Property(x => x.Activation).HasMaxLength(32);
            e.Property(x => x.LinkedGuardrailIdsJson).HasColumnType("jsonb");
            e.HasIndex(x => x.AppId);
            e.HasIndex(x => x.SortOrder);
        });

        modelBuilder.Entity<AgenticAppGuardrailEntity>(e =>
        {
            e.ToTable("agentic_app_guardrails");
            e.HasKey(x => new { x.AppId, x.Id });
            e.Property(x => x.AppId).HasMaxLength(64);
            e.Property(x => x.Id).HasMaxLength(128);
            e.Property(x => x.Name).HasMaxLength(256);
            e.Property(x => x.Description).HasColumnType("text");
            e.Property(x => x.Kind).HasMaxLength(64);
            e.Property(x => x.ConfigJson).HasColumnType("jsonb");
            e.HasIndex(x => x.AppId);
            e.HasIndex(x => x.Kind);
            e.HasIndex(x => x.SortOrder);
        });
    }
}

public sealed class AgenticSkillCatalogEntity
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string PromptMarkdown { get; set; } = string.Empty;
    public string Category { get; set; } = "general";
    public string Activation { get; set; } = "skill";
    public bool IsSystem { get; set; }
    public bool IsDefaultEnabled { get; set; }
    public int SortOrder { get; set; }
    public string LinkedGuardrailIdsJson { get; set; } = "[]";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class AgenticGuardrailCatalogEntity
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string ConfigJson { get; set; } = "{}";
    public bool IsSystem { get; set; }
    public bool IsDefaultEnabled { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class AgenticAppSkillEntity
{
    public string AppId { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string PromptMarkdown { get; set; } = string.Empty;
    public string Category { get; set; } = "general";
    public string Activation { get; set; } = "skill";
    public bool IsEnabled { get; set; } = true;
    public int SortOrder { get; set; }
    public string LinkedGuardrailIdsJson { get; set; } = "[]";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class AgenticAppGuardrailEntity
{
    public string AppId { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string ConfigJson { get; set; } = "{}";
    public bool IsEnabled { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class GlobalWikiDocumentEntity
{
    public string AppId { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public string RevisionId { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string MetadataJson { get; set; } = "{}";
    public string ContentHash { get; set; } = string.Empty;
    public string Status { get; set; } = "active";
    public DateTimeOffset ValidFrom { get; set; }
    public DateTimeOffset? ValidTo { get; set; }
    public string? SupersedesRevisionId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class SessionRecordEntity
{
    public string AppId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string DataJson { get; set; } = "{}";
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class McpCredentialEntity
{
    public string AppId { get; set; } = string.Empty;
    public string IntegrationName { get; set; } = string.Empty;
    public string CredentialRef { get; set; } = string.Empty;
    public string AuthMode { get; set; } = string.Empty;
    public string SecretJson { get; set; } = "{}";
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class McpCatalogToolEntity
{
    public string AppId { get; set; } = string.Empty;
    public string IntegrationName { get; set; } = string.Empty;
    public string QualifiedName { get; set; } = string.Empty;
    public string ToolName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string InputSchemaJson { get; set; } = "{}";
    public string CapabilitiesJson { get; set; } = "[]";
    public DateTimeOffset LastSyncedAt { get; set; }
}

public sealed class McpCatalogSyncEntity
{
    public string AppId { get; set; } = string.Empty;
    public string IntegrationName { get; set; } = string.Empty;
    public int ToolCount { get; set; }
    public string SyncStatus { get; set; } = "pending";
    public string? LastError { get; set; }
    public DateTimeOffset LastSyncedAt { get; set; }
}

public sealed class AgenticPendingRecordEntity
{
    public string AppId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string StateJson { get; set; } = "{}";
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class RegisteredAppEntity
{
    public string AppId { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string AppName { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public DateTimeOffset RegisteredAt { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class AppProfileEntity
{
    public string AppId { get; set; } = string.Empty;
    public string Persona { get; set; } = string.Empty;
    public string BusinessRules { get; set; } = string.Empty;
    public string FormatRules { get; set; } = string.Empty;
    public string WikiSchema { get; set; } = string.Empty;
    public string ConfigJson { get; set; } = "{}";
    public DateTimeOffset UpdatedAt { get; set; }
}
