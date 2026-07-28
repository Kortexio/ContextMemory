using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContextMemory.Infrastructure.Migrations;

[DbContext(typeof(Persistence.Postgres.ContextMemoryDbContext))]
[Migration("20260728144500_WidenGlobalWikiSummary")]
public partial class WidenGlobalWikiSummary : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "Summary",
            table: "global_wiki_documents",
            type: "text",
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(512)",
            oldMaxLength: 512);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "Summary",
            table: "global_wiki_documents",
            type: "character varying(512)",
            maxLength: 512,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "text");
    }
}
