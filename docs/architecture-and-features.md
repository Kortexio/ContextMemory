> Part of the ContextMemory docs. [Back to README](../README.md).

## Architecture in 30 seconds

```
Your app (OpenAI-compatible client)
        │
        ▼ POST /v1/chat/completions
┌───────────────────────────────────────────┐
│  ContextMemory Gateway (.NET 9)           │
│  1. Auth + tenant (API key, X-App-Id)     │
│  2. Memory: history + session wiki        │
│  3. Global Wiki via wiki_search (in loop) │
│  4. Web search (optional)                 │
│  5. Agentic loop (wiki ± sandbox ± MCP)   │
│     skills + guardrails + validation/HITL │
│  6. OpenAI-schema response (choices[])    │
└───────┬───────────────┬───────────────────┘
        ▼               ▼
 sandbox-runtime     mcp-runtime / MCP
 (shell/python/node) (HTTP + stdio, OAuth)
 or ACA Dynamic Sessions
```

---

## Features

### Contextual memory (core)

- **Per-session wiki** — Markdown pages, index, execution log, and schema; automatic compaction as volume grows.
- **Recent history** — last N messages injected into the prompt (configurable per tenant).
- **Persona and rules** — `basePersona`, `businessRules`, `formatRules`, `wikiSchema` per application.
- **Zero client changes for memory** — send only the new message; the gateway builds the full prompt.

### Global Wiki (app-scoped knowledge base)

Shared Markdown documents for an entire `appId` (all users/sessions). Unlike session memory, Global Wiki is **not** injected on every turn — when enabled, the model calls the built-in tool `wiki_search` only when it needs documented facts (token-efficient).

| Capability | What it does |
|---|---|
| **Ingest** | `PUT /apps/{appId}/wiki/documents/{documentId}` — idempotent upsert by content hash; batch via `POST .../documents/batch` (storage-only; no LLM on the write path) |
| **Digests** | `POST /apps/{appId}/wiki/digests/rebuild` — LLM digests (`Keywords:` + short bullets) into `summary`; refreshes the `wiki:catalog` document. Use after bulk ingest or with `force: true` to regenerate |
| **List / delete** | `GET` / `DELETE` under `/apps/{appId}/wiki/documents...` |
| **Query** | `POST /apps/{appId}/wiki/query` — keyword/FTS search returning a compact Markdown pack of top matches (supports `asOf` for point-in-time facts) |
| **Revisions / audit** | `GET .../documents/{id}/revisions` timeline; `GET .../wiki/audit?from=&to=` export |
| **Chat** | Tool `wiki_search` in the agentic loop when `GlobalWikiEnabled` is true (default); optional `asOf` |
| **Config** | Toggle / budget via app runtime config (`GlobalWikiEnabled`, max chars) |

### Temporal facts

Global Wiki documents are **revisioned**. Updating content (default) **supersedes** the previous revision: the old row keeps `valid_from`/`valid_to` and `status=superseded`; a new `active` revision is written. Pass `overwrite: true` on upsert for legacy in-place replace.

- `wiki_search` / `/wiki/query` without `asOf` → only facts valid **now**
- `asOf: "2026-03-01T00:00:00Z"` → what was valid at that instant
- Soft delete closes the active validity window (history retained)

```bash
# Point-in-time query
curl -X POST http://localhost:5100/apps/demo-dev/wiki/query \
  -H "Content-Type: application/json" \
  -H "X-App-Id: demo-dev" \
  -H "Authorization: Bearer cm_live_dev_key_change_me" \
  -d '{"query":"KYC status","asOf":"2026-03-01T00:00:00Z","topK":5}'
```

### MCP server (Cursor / Claude Desktop)

**Primary wedge** — see [Give Cursor / Claude permanent memory](#give-cursor--claude-permanent-memory-5-minutes).

Tools: `memory_save`, `memory_search`, `memory_get` (plus `wiki_search`, `session_recall`).

```bash
cd mcp-server && npm install && node print-mcp-config.mjs
```

Paste into Cursor MCP settings. Recordable demo: [`docs/aha-demo.html`](docs/aha-demo.html).

Typical sources: Jira issues, Confluence pages, SQL exports, or any pipeline that emits Markdown with a stable `documentId` (e.g. `jira:PROJ-123`).

```bash
# Upsert a document (app API key + X-App-Id, or admin path as configured)
curl -X PUT http://localhost:5100/apps/demo-dev/wiki/documents/jira:PROJ-123 \
  -H "Content-Type: application/json" \
  -H "X-App-Id: demo-dev" \
  -H "Authorization: Bearer cm_live_dev_key_change_me" \
  -d '{
    "title": "PROJ-123 — Fix renewal invoice",
    "content": "# PROJ-123\n\n## Description\n...",
    "sourceId": "jira:PROJ",
    "summary": "Billing renewal invoice bug"
  }'

# After a bulk ingest, rebuild LLM digests + wiki:catalog
curl -X POST http://localhost:5100/apps/demo-dev/wiki/digests/rebuild \
  -H "Content-Type: application/json" \
  -H "X-App-Id: demo-dev" \
  -H "Authorization: Bearer cm_live_dev_key_change_me" \
  -d '{"force": false}'

# Search without chat
curl -X POST http://localhost:5100/apps/demo-dev/wiki/query \
  -H "Content-Type: application/json" \
  -H "X-App-Id: demo-dev" \
  -H "Authorization: Bearer cm_live_dev_key_change_me" \
  -d '{"query":"subscription renewal invoice","topK":5}'
```

How that connects to chat: see **[Retrieval + agentic loop](#retrieval--agentic-loop)** — `wiki_search` is a loop tool, not a parallel RAG path.

### Retrieval + agentic loop

Global Wiki retrieval and the agentic gateway are **one pipeline**, not two products bolted together.

| Layer | What happens |
|---|---|
| **Session memory** | Always: recent history + compiled per-session wiki injected into the prompt (no tool call). |
| **Global Wiki** | On demand: the model emits `tool_calls` → `wiki_search` → compact Markdown pack of top matches. |
| **Other tools** | Optional: shell/python/node (sandbox/ACA), MCP (`server__tool`), web search (heuristic / always / …). |
| **Skills / guardrails** | Shape the loop: e.g. `wiki-first-for-docs` steers toward `wiki_search`; validators can reject ungrounded finals. |

**When the loop runs**

```text
AgenticEnabled =
    (agentic.enabled && has execution/MCP tools)
    || GlobalWikiEnabled          // default true
```

So a tenant with **only** Global Wiki still enters the agentic loop so the model can call `wiki_search`. Turning `agentic.enabled` on without tools does **not** by itself enable the loop unless Global Wiki (or tools) is also available.

**End-to-end flow**

```text
1. Ingest     PUT/batch  /apps/{id}/wiki/documents…     (storage only)
2. Digests    POST       /apps/{id}/wiki/digests/rebuild (LLM summary + wiki:catalog)
3. Chat       POST       /v1/chat/completions
              │
              ├─ build prompt (persona + session wiki + history + skills)
              ├─ expose tools: wiki_search [+ sandbox/MCP if configured]
              ├─ loop: LLM ↔ tool_calls ↔ observations ↔ validation / HITL
              └─ return OpenAI choices[] (client never sees internal tool chatter)
```

**Same request, grounded + action**

A single user turn can:

1. Call `wiki_search` for ticket/policy facts  
2. Call an MCP tool (e.g. Zuora) or sandbox code with that evidence  
3. Pass guardrails / HITL if configured  
4. Answer from tool output only  

Clients keep sending a normal chat body; they do not implement retrieval or orchestration.

**Configure the combo (Admin)**

1. **Config → Global Wiki** — enabled + tool char budget  
2. **Config → Agentic** — enable if you also want sandbox/MCP; pick skills (`wiki-first-for-docs`, …) and guardrail packs  
3. Ingest docs + `digests/rebuild` for large corpora  
4. Smoke in **Chat Lab** (or `POST /v1/chat/completions`) with a question that should hit the wiki  

**Not the same as**

| | Session wiki | Global Wiki (`wiki_search`) | Classic RAG inject |
|---|---|---|---|
| Scope | One user session | Whole `appId` | N/A (we don't do this) |
| In prompt every turn? | Yes (budgeted) | No — tool on demand | N/A (we don't do this) |
| Needs agentic loop? | No | **Yes** | N/A (we don't do this) |

### Agentic Gateway

- **Same endpoint** — preferred `POST /v1/chat/completions` (legacy `POST /api/chat`). Loop runs when `AgenticEnabled` is true: `(agentic.enabled && tools) || GlobalWikiEnabled` — see [Retrieval + agentic loop](#retrieval--agentic-loop).
- **Orchestrator** — loop with iteration cap, configurable timeout, and validation before returning the final answer. Built-in `wiki_search` participates like any other tool when Global Wiki is on.
- **Skills & guardrail packs (two levels)** — platform catalog (File or Postgres), seeded on startup, plus optional per-app inventory.
  - **Platform** — Markdown skills + guardrail packs with `IsDefaultEnabled`; apply to **all** apps. Admin **Skills** (`/skills`) or `/admin/agentic/skills|guardrails`.
  - **Per app** — CRUD under `/apps/{appId}/policies` or `/admin/apps/{appId}/skills|guardrails`. `IsEnabled` toggles; additive to platform defaults (apps cannot turn off platform packs).
  - **Runtime** — union of platform default-on + app enabled. Skill `sandbox-facts-selfhosted` only when a self-hosted sandbox tool is configured.
  - Legacy `agentic.policyPacks` is ignored.
  - Distinct from loop **`guardrails`** below (`maxIterations`, HITL keywords, egress, validation mode) — those remain numeric/policy knobs on the orchestrator.
- **Execution tools**
  - ACA Dynamic Sessions: `shell_execute`, `python_execute`, `node_execute`, `container_execute` (custom image)
  - Self-hosted sandbox (`self-hosted-sandbox`): same tool names against **sandbox-runtime** (Compose) or your own gVisor/sandbox endpoint
- **Integration tools (MCP)**
  - Per-app MCP catalog with **HTTP** and **stdio** transports (`mcp-runtime` hosts stdio packages under `/opt/mcps`)
  - Qualified naming `server__tool`; rebuild with `POST /apps/{appId}/mcp/catalog/rebuild`
  - Credentials via `POST /apps/{appId}/mcp/credentials/{name}`; test with `POST .../mcp/test/{name}`
  - `mcp_servers` pass-through for backends that support it natively
  - Authentication: bearer, api-key, or **per-tenant OAuth client-credentials**
- **Per-tenant loop guardrails** (`agentic.guardrails`)
  - `maxIterations`, `loopTimeoutSeconds`
  - `requireConfirmationFor` — keywords that trigger HITL **before** execution
  - `networkEgress: restricted` — blocks external endpoints except allowlist/`allowEgress`
  - `validationMode`: `deterministic` | `hybrid` | `llm-judge`
  - `requireZeroExitCode`, `expectedAnswerPatterns` (regex), `blockedAnswerPatterns`
  - `humanReviewOnMaxIterations` — human review when the loop exhausts iterations
- **Human-in-the-loop**
  - Blocks before destructive tools; state persisted per session
  - Response includes `[CONFIRM:id]`; user confirms with natural language or explicit token
  - Checkpoint in session wiki `log.md` (`agentic-checkpoint`)
  - Human review of partial answers when max iterations is reached
- **Prompt profiles** — `auto`, `ollama`, `openai`, `claude` for system prompts, tool descriptions, and observations tuned to backend/model.

### Streaming and latency

- With `stream: true`, tool calls and internal observations **do not leak** into the text stream.
- The final answer is emitted in chunks after the loop completes (or times out).
- Agentic progress metadata via `context_memory.agentic`.
- Timeout with graceful partial response + header `X-Context-Memory-Agentic-Timed-Out`.

### Web search

- Providers: **Tavily**, **Brave**
- Modes: `heuristic`, `llm`, `always`, `off`
- Ephemeral context in the prompt; optional persistence of facts to the wiki
- Response headers: `X-Web-Search-Used`, `X-Web-Search-Provider`, etc.

### Multi-tenant and operations

- **Isolated apps** — API key, config, tools, and guardrails per `appId`
- **Rate limiting** — RPM/TPM per app and per user; extra weight for agentic turns
- **Telemetry** — requests, tokens, latency, wiki, web search, active users
- **Admin** — Blazor console at `ContextMemory.Admin.Web` (`:5200`); see [Admin UI guide](admin-ui.md)
- **Persistence**
  - `File` — development and single-node
  - `Postgres` — apps, profiles, **sessions/wiki**, and HITL state in JSONB (multi-instance HA)

### Supported LLM backends

| Backend value | Wire protocol | Host default URL |
|---|---|---|
| `ollama` (default) | OpenAI `/v1` on `OllamaEndpoint` | `ContextMemory:OllamaEndpoint` |
| `vllm` / `openai-compatible` / `openai` / `custom` | OpenAI `/v1/chat/completions` | `ContextMemory:OpenAiEndpoint` or per-app `llmEndpoint` |
| `lmstudio` | OpenAI `/v1` | `ContextMemory:LmStudioEndpoint` |
| `ollama-native` | Ollama `/api/chat` (fallback) | `ContextMemory:OllamaEndpoint` |

**Per-app overrides** (Admin → app → Config, or `PATCH /admin/apps/{id}/config`):

| Field | Meaning |
|---|---|
| `llmBackend` | Which protocol to use |
| `llmModel` | Model id sent to that backend |
| `llmEndpoint` | Optional base URL for this app only. Empty = host default above. For OpenAI-compatible URLs, `/v1` is appended if missing. |
| `llmApiKey` | Optional API key for this app. Empty = host `OpenAiApiKey`. |

Example — point one tenant at a remote vLLM / LiteLLM server:

```json
{
  "llmBackend": "openai-compatible",
  "llmModel": "my-model",
  "llmEndpoint": "http://gpu-box:8000",
  "llmApiKey": "optional-key"
}
```

---

## Agentic configuration (example)

```json
{
  "agentic": {
    "enabled": true,
    "promptProfile": "auto",
    "policyPacks": {
      "enabledSkillIds": [
        "anti-hallucination-web",
        "prefer-mcp-over-adhoc",
        "wiki-first-for-docs",
        "zuora-graphql-discover-first"
      ],
      "enabledGuardrailIds": [
        "url-fetch-required",
        "sandbox-claim-reject",
        "require-error-disclosure",
        "block-credential-leak"
      ]
    },
    "tools": {
      "execution": [
        { "type": "aca-session", "runtime": "shell", "poolEndpoint": "https://pool.eastus.dynamicsessions.io/..." },
        { "type": "aca-session", "runtime": "python", "poolEndpoint": "https://pool.eastus.dynamicsessions.io/..." },
        { "type": "aca-session", "runtime": "custom", "poolEndpoint": "https://pool.eastus.dynamicsessions.io/...", "containerImage": "myregistry.azurecr.io/agent-tools:1.0" }
      ],
      "integrations": [
        {
          "type": "mcp",
          "name": "zuora-mcp",
          "url": "https://internal/zuora-mcp",
          "authMode": "oauth-per-tenant",
          "allowEgress": true,
          "oauth": {
            "tokenUrl": "https://login.example.com/oauth/token",
            "clientId": "client-id",
            "clientSecret": "client-secret",
            "scope": "mcp.read"
          }
        }
      ]
    },
    "guardrails": {
      "maxIterations": 15,
      "loopTimeoutSeconds": 120,
      "validationMode": "hybrid",
      "requireConfirmationFor": ["delete", "deploy-prod"],
      "networkEgress": "restricted",
      "allowedEgressHosts": ["internal.example.com"],
      "requireZeroExitCode": true,
      "expectedAnswerPatterns": ["^## Summary"],
      "humanReviewOnMaxIterations": true
    }
  }
}
```

Configure via the Admin UI (`/apps/{appId}/config` on `:5200` — checkboxes under **Skills & guardrail packs**) or `PATCH /admin/apps/{appId}/config` with the Master Key.

### Seed catalog (defaults)

| Type | Id | Default on | Role |
|---|---|---|---|
| Skill | `anti-hallucination-web` | ✅ | Fetch URLs before describing sites/APIs |
| Skill | `sandbox-facts-selfhosted` | ✅* | Correct self-hosted sandbox capabilities (*only if that tool is configured) |
| Skill | `prefer-mcp-over-adhoc` | ✅ | Prefer MCP tools over hand-rolled HTTP/OAuth |
| Skill | `tool-calling-discipline` | ✅ | When/how to emit `tool_calls` |
| Skill | `ground-answers-in-evidence` | ✅ | No invented numbers/IDs |
| Skill | `wiki-first-for-docs` | ✅ | Prefer `wiki_search` for ingested docs |
| Skill | `clarify-when-ambiguous` | ✅ | Ask when the goal is underspecified |
| Skill | `privacy-and-secrets` | ✅ | Redact credentials |
| Skill | `transparent-failures` | ✅ | Report tool errors honestly |
| Skill | `concise-professional` | ✅ | Direct answers, less filler |
| Skill | `strict-no-speculation` | ❌ | Opt-in: refuse claims without evidence |
| Skill | `step-by-step-reasoning-brief` | ❌ | Opt-in: short plan before tools |
| Skill | `zuora-graphql-discover-first` | ❌ | Opt-in: discover Zuora GraphQL schema before queries |
| Guardrail | `url-fetch-required` | ✅ | Reject URL descriptions without fetch evidence |
| Guardrail | `sandbox-claim-reject` | ✅ | Reject false ACA/no-network claims |
| Guardrail | `require-error-disclosure` | ✅ | Reject answers that ignore failed tool steps |
| Guardrail | `block-credential-leak` | ✅ | Reject secret-like substrings in the answer |

Custom skills (non-system) can be created, imported, and exported; system seeds are updated in place on catalog seed but remain selectable per app.
---

