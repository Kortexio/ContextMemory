# ContextMemory MCP — permanent memory for Cursor / Claude

**Wedge:** give your coding agent durable memory with one MCP config.

## 5-minute aha

### 1. Start the gateway (Docker)

```bash
docker run --rm -p 5100:8080 \
  -v contextmemory-data:/app/data \
  -e ContextMemory__MasterKey=cm_master_dev_key_change_me \
  -e ContextMemory__Apps__demo-dev__ApiKey=cm_live_dev_key_change_me \
  -e ContextMemory__Apps__demo-dev__LlmModel=qwen3.5:9b \
  -e ContextMemory__OllamaEndpoint=http://host.docker.internal:11434 \
  --add-host=host.docker.internal:host-gateway \
  ghcr.io/kortexio/contextmemory:latest
```

(Or use Kortexio Cloud and skip Docker — set `CONTEXTMEMORY_BASE_URL` + your `cmk_live_` key.)

### 2. Install MCP deps + print Cursor config

```bash
cd mcp-server
npm install
node print-mcp-config.mjs
```

Paste the JSON into **Cursor → Settings → MCP**.

### 3. Prove it

| Chat | Say | Expect |
|---|---|---|
| A | `Remember: staging DB is postgres-staging-01` | Agent calls `memory_save` |
| B (new) | `What is our staging DB?` | Agent calls `memory_search` and answers |

Tools: `memory_save`, `memory_search`, `memory_get` (+ `wiki_search`, `session_recall`).
