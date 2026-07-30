using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContextMemory.Infrastructure.Migrations;

[DbContext(typeof(Persistence.Postgres.ContextMemoryDbContext))]
[Migration("20260729220000_AddAgenticPolicyCatalog")]
public partial class AddAgenticPolicyCatalog : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "agentic_skill_catalog",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                Description = table.Column<string>(type: "text", nullable: false),
                PromptMarkdown = table.Column<string>(type: "text", nullable: false),
                Category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                IsDefaultEnabled = table.Column<bool>(type: "boolean", nullable: false),
                SortOrder = table.Column<int>(type: "integer", nullable: false),
                LinkedGuardrailIdsJson = table.Column<string>(type: "jsonb", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_agentic_skill_catalog", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "agentic_guardrail_catalog",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                Description = table.Column<string>(type: "text", nullable: false),
                Kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ConfigJson = table.Column<string>(type: "jsonb", nullable: false),
                IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                IsDefaultEnabled = table.Column<bool>(type: "boolean", nullable: false),
                SortOrder = table.Column<int>(type: "integer", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_agentic_guardrail_catalog", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_agentic_skill_catalog_SortOrder",
            table: "agentic_skill_catalog",
            column: "SortOrder");

        migrationBuilder.CreateIndex(
            name: "IX_agentic_guardrail_catalog_Kind",
            table: "agentic_guardrail_catalog",
            column: "Kind");

        migrationBuilder.CreateIndex(
            name: "IX_agentic_guardrail_catalog_SortOrder",
            table: "agentic_guardrail_catalog",
            column: "SortOrder");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "agentic_skill_catalog");
        migrationBuilder.DropTable(name: "agentic_guardrail_catalog");
    }
}
