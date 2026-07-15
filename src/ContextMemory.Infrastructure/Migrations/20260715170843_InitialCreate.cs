using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContextMemory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agentic_pending_records",
                columns: table => new
                {
                    AppId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SessionId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    StateJson = table.Column<string>(type: "jsonb", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agentic_pending_records", x => new { x.AppId, x.UserId, x.SessionId });
                });

            migrationBuilder.CreateTable(
                name: "app_profiles",
                columns: table => new
                {
                    AppId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Persona = table.Column<string>(type: "text", nullable: false),
                    BusinessRules = table.Column<string>(type: "text", nullable: false),
                    FormatRules = table.Column<string>(type: "text", nullable: false),
                    WikiSchema = table.Column<string>(type: "text", nullable: false),
                    ConfigJson = table.Column<string>(type: "jsonb", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_app_profiles", x => x.AppId);
                });

            migrationBuilder.CreateTable(
                name: "registered_apps",
                columns: table => new
                {
                    AppId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ApiKey = table.Column<string>(type: "text", nullable: false),
                    AppName = table.Column<string>(type: "text", nullable: false),
                    Domain = table.Column<string>(type: "text", nullable: false),
                    RegisteredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_registered_apps", x => x.AppId);
                });

            migrationBuilder.CreateTable(
                name: "session_records",
                columns: table => new
                {
                    AppId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SessionId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DataJson = table.Column<string>(type: "jsonb", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_session_records", x => new { x.AppId, x.UserId, x.SessionId });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agentic_pending_records");

            migrationBuilder.DropTable(
                name: "app_profiles");

            migrationBuilder.DropTable(
                name: "registered_apps");

            migrationBuilder.DropTable(
                name: "session_records");
        }
    }
}
