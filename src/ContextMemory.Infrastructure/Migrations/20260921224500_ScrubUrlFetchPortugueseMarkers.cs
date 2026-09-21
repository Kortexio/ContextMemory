using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContextMemory.Infrastructure.Migrations;

/// <summary>
/// url-fetch aboutSiteMarkers match the user objective. Platform seed is EN-only;
/// Portuguese intent phrases belong in app-scoped ConfigJson if needed before request translation.
/// </summary>
[DbContext(typeof(Persistence.Postgres.ContextMemoryDbContext))]
[Migration("20260921224500_ScrubUrlFetchPortugueseMarkers")]
public partial class ScrubUrlFetchPortugueseMarkers : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE agentic_guardrail_catalog
            SET "ConfigJson" = '{
                  "kind": "url-fetch",
                  "feedback": "Rejected: you described a website/URL without fetching it. Hosts in the user message: {hosts}. Emit tool_calls first, then answer ONLY from tool output.",
                  "aboutSiteMarkers": ["this site","this website","this page","this url","this link","the website","the site","what is","what''s this","whats this","what about","open ","visit ","fetch","scrape","summary","content of"],
                  "fetchToolMarkers": ["python_execute","shell_execute","node_execute","web_search","fetch_url","http_request","browser_navigate","browser_snapshot","browser_screenshot","read_image","brave","tavily","ddgs","duckduckgo","playwright","httpx","requests","curl"]
                }'::jsonb,
                "UpdatedAt" = NOW()
            WHERE "Id" = 'url-fetch-required' AND "IsSystem" = TRUE;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Irreversible EN scrub — no-op.
    }
}
