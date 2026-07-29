using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContextMemory.Infrastructure.Migrations;

[DbContext(typeof(Persistence.Postgres.ContextMemoryDbContext))]
[Migration("20260729110000_AddMcpCatalogAndCredentials")]
public partial class AddMcpCatalogAndCredentials : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "mcp_catalog_sync",
            columns: table => new
            {
                AppId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                IntegrationName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                ToolCount = table.Column<int>(type: "integer", nullable: false),
                SyncStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                LastError = table.Column<string>(type: "text", nullable: true),
                LastSyncedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_mcp_catalog_sync", x => new { x.AppId, x.IntegrationName });
            });

        migrationBuilder.CreateTable(
            name: "mcp_catalog_tools",
            columns: table => new
            {
                AppId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                IntegrationName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                QualifiedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                ToolName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Description = table.Column<string>(type: "text", nullable: false),
                InputSchemaJson = table.Column<string>(type: "jsonb", nullable: false),
                CapabilitiesJson = table.Column<string>(type: "jsonb", nullable: false),
                LastSyncedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_mcp_catalog_tools", x => new { x.AppId, x.IntegrationName, x.QualifiedName });
            });

        migrationBuilder.CreateTable(
            name: "mcp_credentials",
            columns: table => new
            {
                AppId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                IntegrationName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                CredentialRef = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                AuthMode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                SecretJson = table.Column<string>(type: "jsonb", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_mcp_credentials", x => new { x.AppId, x.IntegrationName, x.CredentialRef });
            });

        migrationBuilder.CreateIndex(
            name: "IX_mcp_catalog_sync_AppId",
            table: "mcp_catalog_sync",
            column: "AppId");

        migrationBuilder.CreateIndex(
            name: "IX_mcp_catalog_tools_AppId",
            table: "mcp_catalog_tools",
            column: "AppId");

        migrationBuilder.CreateIndex(
            name: "IX_mcp_catalog_tools_AppId_IntegrationName",
            table: "mcp_catalog_tools",
            columns: new[] { "AppId", "IntegrationName" });

        migrationBuilder.CreateIndex(
            name: "IX_mcp_credentials_AppId",
            table: "mcp_credentials",
            column: "AppId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "mcp_catalog_sync");
        migrationBuilder.DropTable(name: "mcp_catalog_tools");
        migrationBuilder.DropTable(name: "mcp_credentials");
    }
}
