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
    }
}

public sealed class SessionRecordEntity
{
    public string AppId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string DataJson { get; set; } = "{}";
    public DateTimeOffset UpdatedAt { get; set; }
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
