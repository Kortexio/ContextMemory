using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContextMemory.Infrastructure.Migrations;

[DbContext(typeof(Persistence.Postgres.ContextMemoryDbContext))]
[Migration("20260803120000_AddGlobalWikiTemporalAndFts")]
public partial class AddGlobalWikiTemporalAndFts : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "RevisionId",
            table: "global_wiki_documents",
            type: "character varying(64)",
            maxLength: 64,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "Status",
            table: "global_wiki_documents",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "active");

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "ValidFrom",
            table: "global_wiki_documents",
            type: "timestamp with time zone",
            nullable: false,
            defaultValue: new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero));

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "ValidTo",
            table: "global_wiki_documents",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "SupersedesRevisionId",
            table: "global_wiki_documents",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        // Backfill revision ids and validity from existing rows.
        migrationBuilder.Sql("""
            UPDATE global_wiki_documents
            SET "RevisionId" = replace(gen_random_uuid()::text, '-', ''),
                "Status" = 'active',
                "ValidFrom" = "CreatedAt"
            WHERE "RevisionId" = '' OR "RevisionId" IS NULL;
            """);

        migrationBuilder.DropPrimaryKey(
            name: "PK_global_wiki_documents",
            table: "global_wiki_documents");

        migrationBuilder.AddPrimaryKey(
            name: "PK_global_wiki_documents",
            table: "global_wiki_documents",
            columns: new[] { "AppId", "DocumentId", "RevisionId" });

        migrationBuilder.CreateIndex(
            name: "IX_global_wiki_documents_AppId_DocumentId_Status",
            table: "global_wiki_documents",
            columns: new[] { "AppId", "DocumentId", "Status" });

        migrationBuilder.CreateIndex(
            name: "IX_global_wiki_documents_AppId_ValidFrom_ValidTo",
            table: "global_wiki_documents",
            columns: new[] { "AppId", "ValidFrom", "ValidTo" });

        // Generated tsvector + GIN for FTS (Phase B).
        migrationBuilder.Sql("""
            ALTER TABLE global_wiki_documents
            ADD COLUMN IF NOT EXISTS search_vector tsvector
            GENERATED ALWAYS AS (
              to_tsvector(
                'simple',
                coalesce("DocumentId",'') || ' ' ||
                coalesce("Slug",'') || ' ' ||
                coalesce("Title",'') || ' ' ||
                coalesce("Summary",'') || ' ' ||
                coalesce("SourceId",'') || ' ' ||
                coalesce("Content",'')
              )
            ) STORED;
            """);

        migrationBuilder.Sql("""
            CREATE INDEX IF NOT EXISTS IX_global_wiki_documents_search_vector
            ON global_wiki_documents USING GIN (search_vector);
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""DROP INDEX IF EXISTS IX_global_wiki_documents_search_vector;""");
        migrationBuilder.Sql("""ALTER TABLE global_wiki_documents DROP COLUMN IF EXISTS search_vector;""");

        migrationBuilder.DropIndex(
            name: "IX_global_wiki_documents_AppId_ValidFrom_ValidTo",
            table: "global_wiki_documents");

        migrationBuilder.DropIndex(
            name: "IX_global_wiki_documents_AppId_DocumentId_Status",
            table: "global_wiki_documents");

        migrationBuilder.DropPrimaryKey(
            name: "PK_global_wiki_documents",
            table: "global_wiki_documents");

        migrationBuilder.DropColumn(name: "SupersedesRevisionId", table: "global_wiki_documents");
        migrationBuilder.DropColumn(name: "ValidTo", table: "global_wiki_documents");
        migrationBuilder.DropColumn(name: "ValidFrom", table: "global_wiki_documents");
        migrationBuilder.DropColumn(name: "Status", table: "global_wiki_documents");
        migrationBuilder.DropColumn(name: "RevisionId", table: "global_wiki_documents");

        migrationBuilder.AddPrimaryKey(
            name: "PK_global_wiki_documents",
            table: "global_wiki_documents",
            columns: new[] { "AppId", "DocumentId" });
    }
}
