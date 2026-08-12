# azure-monitor-mcp (ContextMemory inbound)

Stdio MCP package for **Azure Log Analytics** KQL. Drop under `/opt/mcps` (or this repo path) and register as an inbound integration.

## Tools

| Tool | Purpose |
|---|---|
| `azure_logs_query` | Arbitrary KQL |
| `azure_logs_get_timeline` | HTTP/trace/exception timeline for resource + entity |
| `azure_logs_search_traces` | Search AppTraces / AppExceptions |

## Auth (Admin MCP credentials `Env`)

| Variable | Required |
|---|---|
| `AZURE_TENANT_ID` | yes |
| `AZURE_CLIENT_ID` | yes |
| `AZURE_CLIENT_SECRET` | yes |
| `LOG_ANALYTICS_WORKSPACE_ID` | default workspace (or pass `workspace_id` per call) |

Service principal needs **Log Analytics Reader** (or equivalent) on the workspace.

## Register in ContextMemory

```json
{
  "type": "mcp",
  "name": "azure-monitor",
  "transport": "stdio",
  "command": "node",
  "args": ["/opt/mcps/azure-monitor-mcp/src/index.mjs"],
  "credentialRef": "azure-sp",
  "enabled": true
}
```

Then:

```http
POST /apps/{appId}/mcp/credentials/azure-monitor
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
POST /apps/{appId}/mcp/catalog/rebuild
```

Qualified tool names: `azure-monitor__azure_logs_query`, etc.

## Docker / mcp-runtime

Mount or copy this folder to `/opt/mcps/azure-monitor-mcp` on the **mcp-runtime** container (same pattern as Zuora under `/opt/mcps`).

See [inbound-mcp-guide.md](../../../docs/inbound-mcp-guide.md) for MCP-first + sandbox `az` fallback (same credentials injected into sandbox execute).

## Local smoke

```bash
node src/index.mjs
# speak MCP Content-Length JSON-RPC on stdin (or use ContextMemory mcp/test)
```

Zero npm dependencies (Node 18+ `fetch`).
