using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContextMemory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGlobalWikiDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "global_wiki_documents",
                columns: table => new
                {
                    AppId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DocumentId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Slug = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    Summary = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    SourceId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    MetadataJson = table.Column<string>(type: "jsonb", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_global_wiki_documents", x => new { x.AppId, x.DocumentId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_global_wiki_documents_AppId",
                table: "global_wiki_documents",
                column: "AppId");

            migrationBuilder.CreateIndex(
                name: "IX_global_wiki_documents_AppId_SourceId",
                table: "global_wiki_documents",
                columns: new[] { "AppId", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_global_wiki_documents_AppId_UpdatedAt",
                table: "global_wiki_documents",
                columns: new[] { "AppId", "UpdatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "global_wiki_documents");
        }
    }
}
