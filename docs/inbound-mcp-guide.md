> Part of the ContextMemory docs. [Back to README](../README.md).

# Inbound MCP guide (ops / TriageHub)

How to wire **external tools** into the ContextMemory **agent** (Azure Monitor, GitHub, Atlassian, Zuora, …).

This is **not** the Cursor wedge (`mcp-server/` → `memory_save` / `wiki_search`). That path is **outbound**: the IDE talks to ContextMemory. Here the gateway talks to **your** MCP servers.

## Cursor MCP IDs ≠ ContextMemory inbound catalog

| Concept | Where it lives | Example |
|---|---|---|
| Cursor / IDE MCP | Extension host on the developer machine | `user-eamodio.gitlens-…`, `plugin-atlassian-atlassian` |
| ContextMemory **inbound** | `agentic.tools.integrations[]` + `mcp-runtime` | HTTP URL or stdio `command`/`args` on the **mcp-runtime** host |

**Anti-patterns (do not use in Admin):**

```json
{ "name": "gitlens", "source": "user-eamodio.gitlens-extension-GitKraken" }
{ "name": "atlassian", "source": "plugin-atlassian-atlassian" }
```

There is no `source` field. Valid shape:

```json
{
  "type": "mcp",
  "name": "azure-monitor",
  "transport": "stdio",
  "command": "node",
  "args": ["/opt/mcps/azure-monitor-mcp/dist/index.js"],
  "credentialRef": "azure-sp",
  "enabled": true
}
```

Or HTTP:

```json
{
  "type": "mcp",
  "name": "my-http-mcp",
  "transport": "http",
  "url": "http://my-mcp:8080/mcp",
  "credentialRef": "http-bearer",
  "enabled": true
}
```

After changing integrations: `POST /apps/{appId}/mcp/catalog/rebuild` (or Admin → MCP rebuild).

## Where to store secrets (Azure + Git)

| Admin surface | Stores | Use for Azure / Git? |
|---|---|---|
| `/apps/{appId}/credentials` | ContextMemory **API key** only | **No** |
| `POST /apps/{appId}/mcp/credentials/{name}` (+ `credentialRef` on the integration) | Bearer / OAuth / **`Env`** map | **Yes** |

### Convention: one integration name → primary MCP + sandbox fallback

| Integration `name` | Typical `Env` keys | Primary | Sandbox fallback |
|---|---|---|---|
| `azure-monitor` | `AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET`, `LOG_ANALYTICS_WORKSPACE_ID` | MCP tools `azure_logs_*` | `az monitor log-analytics query` |
| `github` (alias `git`) | `GITHUB_TOKEN` or `GH_TOKEN` | GitHub MCP / `gh` stdio | `git` / `gh` in sandbox |

Upsert example (Master Key / app path as configured):

```http
POST /apps/{appId}/mcp/credentials/azure-monitor
Content-Type: application/json

{
  "credentialRef": "azure-sp",
  "authMode": "env",
  "env": {
    "AZURE_TENANT_ID": "…",
    "AZURE_CLIENT_ID": "…",
    "AZURE_CLIENT_SECRET": "…",
    "LOG_ANALYTICS_WORKSPACE_ID": "…"
  }
}
```

```http
POST /apps/{appId}/mcp/credentials/github
Content-Type: application/json

{
  "credentialRef": "gh-pat",
  "authMode": "env",
  "env": {
    "GITHUB_TOKEN": "ghp_…"
  }
}
```

The gateway injects that `Env` into:

1. **MCP stdio** processes (already), and  
2. **Sandbox** `shell_execute` / `python_execute` / `node_execute` (fallback) for integrations named `azure-monitor`, `github`, or `git`.

Compose `AZURE_*` / `GITHUB_TOKEN` on the `sandbox-runtime` service is optional **lab bootstrap** only when Admin credentials are not set yet.

## MCP-first + sandbox fallback

```text
Need Log Analytics evidence?
  → Prefer azure-monitor__azure_logs_* (MCP)
  → Else shell_execute: az monitor log-analytics query … -o json
  → Else abstain / HITL (do not invent timelines)

Need code evidence?
  → Prefer github MCP / gh tools
  → Else shell_execute: git / gh (token from same credential store)
  → HITL before git push / writes
```

Canonical fallback commands:

```bash
# Logs (requires az CLI in sandbox image + AZURE_* env)
az login --service-principal -u "$AZURE_CLIENT_ID" -p "$AZURE_CLIENT_SECRET" --tenant "$AZURE_TENANT_ID"
az monitor log-analytics query \
  --workspace "$LOG_ANALYTICS_WORKSPACE_ID" \
  --analytics-query "AppServiceHTTPLogs | take 20" \
  -o json

# Git (git is preinstalled; needs GITHUB_TOKEN for private HTTPS clones)
git clone "https://x-access-token:${GITHUB_TOKEN}@github.com/org/repo.git"
```

Enable skill **`ops-triage-evidence-first`** (opt-in) so the agent follows this order. Prefer guardrail **`live-data-evidence-required`** (default on when wiki/MCP exist) and optionally **`numeric-grounding`**.

Package drop-in: [`mcp-runtime/mcps/azure-monitor-mcp`](../mcp-runtime/mcps/azure-monitor-mcp/README.md).

## Confluence → Global Wiki ingest (no native connector)

ContextMemory has **no** built-in Confluence connector. Use either:

1. **Live MCP** — host an Atlassian-compatible MCP as inbound HTTP/stdio (not the Cursor plugin id), or  
2. **Batch ingest** into Global Wiki (recommended for glossaries / runbooks).

Ingest (storage only — no LLM on write):

```bash
curl -X PUT "http://localhost:5100/apps/demo-dev/wiki/documents/confluence:123456" \
  -H "Content-Type: application/json" \
  -H "X-App-Id: demo-dev" \
  -H "Authorization: Bearer $CM_API_KEY" \
  -d '{
    "title": "Managed By glossary",
    "content": "# Managed By\n\nEquals Invoice Owner in Zuora…",
    "sourceId": "confluence:SF",
    "summary": "PACCAR glossary: Managed By"
  }'
```

After bulk ingest:

```bash
curl -X POST "http://localhost:5100/apps/demo-dev/wiki/digests/rebuild" \
  -H "Content-Type: application/json" \
  -H "X-App-Id: demo-dev" \
  -H "Authorization: Bearer $CM_API_KEY" \
  -d '{"force": false}'
```

| Prefer | When |
|---|---|
| Live Confluence MCP | ACLs matter; pages change often; one-off lookups |
| Global Wiki ingest | Glossaries/runbooks; lower latency; unified `wiki_search` |

Use stable `documentId` values (`confluence:{pageId}`) so re-ingest supersedes cleanly (temporal revisions).

## Related

- [Architecture and features](architecture-and-features.md) — agentic loop, MCP catalog, harness  
- [Self-host](self-host.md) — Compose (`mcp-runtime`, `sandbox-runtime`)  
- [HITL](hitl.md) — confirmation before destructive tools  
- [Small-model guide](small-model-guide.md) — weak harness + grounding  
