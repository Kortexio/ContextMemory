using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContextMemory.Infrastructure.Migrations;

/// <summary>
/// Scrubs tenant-vendor prose from system skills and guardrail ConfigJson seed rows.
/// Overwrites only IsSystem rows for known catalog ids so Admin customizations on non-system rows stay intact.
/// </summary>
[DbContext(typeof(Persistence.Postgres.ContextMemoryDbContext))]
[Migration("20260921210000_ScrubVendorSpecificCatalogProse")]
public partial class ScrubVendorSpecificCatalogProse : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE agentic_skill_catalog
            SET "PromptMarkdown" = E'## Self-hosted sandbox facts\r\n- `python_execute` / `shell_execute` / `node_execute` run on the **self-hosted sandbox**, not a managed cloud container session.\r\n- Outbound HTTP(S) **works**. Do not claim DNS/network isolation or managed-sandbox restrictions.\r\n- Files are ephemeral (deleted after each call); print results to stdout.\r\n- Reply in the user''s language.',
                "Description" = 'Correct capabilities of the self-hosted sandbox (not a managed cloud sandbox).',
                "UpdatedAt" = NOW()
            WHERE "Id" = 'sandbox-facts-selfhosted' AND "IsSystem" = TRUE;
            """);

        migrationBuilder.Sql("""
            UPDATE agentic_skill_catalog
            SET "PromptMarkdown" = E'## Prefer MCP over ad-hoc HTTP\r\n- When an MCP integration is configured, you MUST use its MCP tools (`server__tool`).\r\n- Do NOT call MCP-backed APIs via `python_execute` / `requests` / `http_request` / hand-rolled OAuth.\r\n- Use `fetch_url` / `web_search` only for allowlisted public HTTP and open-web freshness — never as a substitute for a configured MCP.\r\n- If MCP fails, report the MCP error. Do not fall back to inventing REST scripts or placeholder credentials.\r\n- Do NOT ask the user for client id, client secret, or access tokens when MCP credentials are already configured.\r\n- For product/rules/subscription/account/invoice questions about a live system: after one weak wiki miss, call a configured MCP tool from the catalog instead of repeating `wiki_search`.\r\n- Reply in the user''s language.',
                "UpdatedAt" = NOW()
            WHERE "Id" = 'prefer-mcp-over-adhoc' AND "IsSystem" = TRUE;
            """);

        migrationBuilder.Sql("""
            UPDATE agentic_guardrail_catalog
            SET "Description" = 'Reject false managed-sandbox / no-network claims when self-hosted sandbox is configured.',
                "ConfigJson" = '{
                  "kind": "sandbox-claim",
                  "feedback": "Rejected: you invented false sandbox limitations. This tenant uses self-hosted-sandbox with outbound HTTP. Emit tool_calls — do not narrate hypothetical failures.",
                  "acaMarkers": ["container apps","managed container session","cloud container session","aca dynamic session","aca session","ambiente isolado (aca)","isolated cloud container","isolated managed sandbox"],
                  "noNetworkMarkers": ["não tem acesso à rede","nao tem acesso a rede","sem acesso à rede","sem acesso a rede","não tem acesso a rede","no access to the network","no network access","without network access","cannot access the network","can''t access the network","network egress","rede externa","external network","dns/timeout","dns timeout","falhará com erro de conexão","falhara com erro de conexao","will fail with a connection","não será executado com sucesso","nao sera executado com sucesso","will not be executed successfully","não há como contornar","nao ha como contornar","no way to work around"],
                  "sandboxSubjectMarkers": ["python_execute","shell_execute","node_execute","sandbox","aca"],
                  "hypotheticalMarkers": ["o que aconteceria","what would happen","se eu tentasse","if i tried","if i were to"]
                }'::jsonb,
                "UpdatedAt" = NOW()
            WHERE "Id" = 'sandbox-claim-reject' AND "IsSystem" = TRUE;
            """);

        migrationBuilder.Sql("""
            UPDATE agentic_guardrail_catalog
            SET "ConfigJson" = '{
                  "kind": "live-data-evidence",
                  "feedback": "Rejected: live-data/wiki question without successful MCP/wiki evidence. Emit tool_calls now — prefer wiki_search for tickets/docs or a configured MCP tool (server__tool). Example: {\"tool\":\"wiki_search\",\"arguments\":{\"query\":\"TICKET-123\"}}",
                  "liveDataMarkers": ["account","conta","subscription","subscricao","subscrição","subscricoes","subscrições","assinatura","invoice","fatura","payment","pagamento","billing","canceled","cancelled","cancelad","customer","cliente","balance","saldo","rate plan","query_objects","accountnumber","account number","ticket","tickets","jira","issue","issues","confluence","wiki","validacao","validação"],
                  "evidenceToolMarkers": ["__","query_objects","get_account","manage_customer","wiki_search","wiki_grep","wiki_get","wiki_read"],
                  "honestUnknownMarkers": ["not found","no results","no evidence","could not find","couldn''t find","unable to find","no matching","empty result","tool failed","tool error","failed","não encontrei","nao encontrei","não foi possível","nao foi possivel","sem resultados","sem evidência","sem evidencia","não há dados","nao ha dados","falhou"]
                }'::jsonb,
                "UpdatedAt" = NOW()
            WHERE "Id" = 'live-data-evidence-required' AND "IsSystem" = TRUE;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Irreversible content scrub — no-op.
    }
}
