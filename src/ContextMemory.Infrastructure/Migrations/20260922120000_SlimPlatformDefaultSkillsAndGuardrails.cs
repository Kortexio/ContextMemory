using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContextMemory.Infrastructure.Migrations;

/// <summary>
/// Slim platform defaults: fewer always-on skills/guardrails so models keep judgment room.
/// Remaining catalog rows stay available for Admin/app enablement.
/// </summary>
[DbContext(typeof(Persistence.Postgres.ContextMemoryDbContext))]
[Migration("20260922120000_SlimPlatformDefaultSkillsAndGuardrails")]
public partial class SlimPlatformDefaultSkillsAndGuardrails : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Skills: only the harness nucleus stays default-on.
        migrationBuilder.Sql("""
            UPDATE agentic_skill_catalog
            SET "IsDefaultEnabled" = ("Id" IN (
                    'tool-calling-discipline',
                    'prefer-mcp-over-adhoc',
                    'sandbox-facts-selfhosted',
                    'privacy-and-secrets'
                )),
                "UpdatedAt" = NOW()
            WHERE "IsSystem" = TRUE;
            """);

        // Demote former always-on evidence rules to optional skills.
        migrationBuilder.Sql("""
            UPDATE agentic_skill_catalog
            SET "Activation" = 'skill',
                "UpdatedAt" = NOW()
            WHERE "IsSystem" = TRUE
              AND "Id" IN ('wiki-first-for-docs', 'rule-always-evidence');
            """);

        // Guardrails: safety + harness integrity only.
        migrationBuilder.Sql("""
            UPDATE agentic_guardrail_catalog
            SET "IsDefaultEnabled" = ("Id" IN (
                    'tool-surface-hidden',
                    'thinking-leak',
                    'prompt-injection',
                    'sensitive-pii',
                    'inappropriate-content',
                    'offensive-language',
                    'block-credential-leak',
                    'pre-tool-deny-rm-rf',
                    'post-tool-redact-secrets',
                    'sandbox-claim-reject'
                )),
                "UpdatedAt" = NOW()
            WHERE "IsSystem" = TRUE;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Irreversible policy slim — no-op.
    }
}
