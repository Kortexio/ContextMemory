using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContextMemory.Infrastructure.Migrations;

[DbContext(typeof(Persistence.Postgres.ContextMemoryDbContext))]
[Migration("20260817120000_AddGlobalWikiDigestSearchVector")]
public partial class AddGlobalWikiDigestSearchVector : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE global_wiki_documents
            ADD COLUMN IF NOT EXISTS digest_search_vector tsvector
            GENERATED ALWAYS AS (
              to_tsvector(
                'simple',
                coalesce("DocumentId",'') || ' ' ||
                coalesce("Slug",'') || ' ' ||
                coalesce("Title",'') || ' ' ||
                coalesce("Summary",'') || ' ' ||
                coalesce("SourceId",'')
              )
            ) STORED;
            """);

        migrationBuilder.Sql("""
            CREATE INDEX IF NOT EXISTS IX_global_wiki_documents_digest_search_vector
            ON global_wiki_documents USING GIN (digest_search_vector);
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""DROP INDEX IF EXISTS IX_global_wiki_documents_digest_search_vector;""");
        migrationBuilder.Sql("""ALTER TABLE global_wiki_documents DROP COLUMN IF EXISTS digest_search_vector;""");
    }
}
