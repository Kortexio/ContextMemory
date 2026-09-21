using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContextMemory.Infrastructure.Migrations;

/// <summary>
/// Live-data evidence is app/tenant policy (domain markers), not a platform system guardrail.
/// Removes the system catalog row; apps that need it should define it in the app-scoped catalog.
/// </summary>
[DbContext(typeof(Persistence.Postgres.ContextMemoryDbContext))]
[Migration("20260921223000_RemoveSystemLiveDataEvidenceGuardrail")]
public partial class RemoveSystemLiveDataEvidenceGuardrail : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DELETE FROM agentic_guardrail_catalog
            WHERE "Id" = 'live-data-evidence-required' AND "IsSystem" = TRUE;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Intentionally empty — re-seed only via Admin/app catalog if needed.
    }
}
