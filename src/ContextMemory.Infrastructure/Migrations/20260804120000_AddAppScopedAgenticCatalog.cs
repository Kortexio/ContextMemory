using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContextMemory.Infrastructure.Migrations;

[DbContext(typeof(Persistence.Postgres.ContextMemoryDbContext))]
[Migration("20260804120000_AddAppScopedAgenticCatalog")]
public partial class AddAppScopedAgenticCatalog : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "agentic_app_skills",
            columns: table => new
            {
                AppId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                Description = table.Column<string>(type: "text", nullable: false),
                PromptMarkdown = table.Column<string>(type: "text", nullable: false),
                Category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                SortOrder = table.Column<int>(type: "integer", nullable: false),
                LinkedGuardrailIdsJson = table.Column<string>(type: "jsonb", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_agentic_app_skills", x => new { x.AppId, x.Id });
            });

        migrationBuilder.CreateTable(
            name: "agentic_app_guardrails",
            columns: table => new
            {
                AppId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                Description = table.Column<string>(type: "text", nullable: false),
                Kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ConfigJson = table.Column<string>(type: "jsonb", nullable: false),
                IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                SortOrder = table.Column<int>(type: "integer", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_agentic_app_guardrails", x => new { x.AppId, x.Id });
            });

        migrationBuilder.CreateIndex(
            name: "IX_agentic_app_skills_AppId",
            table: "agentic_app_skills",
            column: "AppId");

        migrationBuilder.CreateIndex(
            name: "IX_agentic_app_skills_SortOrder",
            table: "agentic_app_skills",
            column: "SortOrder");

        migrationBuilder.CreateIndex(
            name: "IX_agentic_app_guardrails_AppId",
            table: "agentic_app_guardrails",
            column: "AppId");

        migrationBuilder.CreateIndex(
            name: "IX_agentic_app_guardrails_Kind",
            table: "agentic_app_guardrails",
            column: "Kind");

        migrationBuilder.CreateIndex(
            name: "IX_agentic_app_guardrails_SortOrder",
            table: "agentic_app_guardrails",
            column: "SortOrder");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "agentic_app_skills");
        migrationBuilder.DropTable(name: "agentic_app_guardrails");
    }
}
