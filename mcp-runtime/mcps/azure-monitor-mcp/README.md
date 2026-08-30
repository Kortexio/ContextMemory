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
# NDJSON (same framing as ContextMemory mcp-runtime)
printf '%s\n' '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"smoke","version":"0"}}}' \
  | node src/index.mjs
# expect a single-line JSON initialize result, then tools/list works the same way
```

Zero npm dependencies (Node 18+ `fetch`).

## Framing note

ContextMemory `mcp-runtime` speaks **NDJSON** on stdio (not Content-Length). This server writes NDJSON and accepts both NDJSON and Content-Length on stdin so catalog rebuild / `mcp/test` does not hang on `initialize`.
