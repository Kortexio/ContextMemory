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
│     + rolling summary + Global digests    │
│  3. Dynamic discovery tools (on demand)   │
│     wiki_search / wiki_grep / artifacts   │
│  4. Web search (optional)                 │
│  5. Agentic loop (sandbox ± MCP ± wiki)   │
│     skills/rules + hooks + HITL           │
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
- **Rolling summary** — mid-turn / session summary kept lean in the system prompt (`## Session summary`); recover detail with `session_log_search` / `artifact_read`.
- **Persona and rules** — `basePersona`, `businessRules`, `formatRules`, `wikiSchema` per application.
- **Zero client changes for memory** — send only the new message; the gateway builds the full prompt.

### Global Wiki (app-scoped knowledge base)

Shared Markdown documents for an entire `appId` (all users/sessions). **Full document bodies are not stuffed into every turn.** Instead:

1. **Digests** (short `Summary`) are injected as a small top-K pack when useful.
2. The model hydrates full pages with `wiki_search` / `wiki_grep` only when needed.

| Capability | What it does |
|---|---|
| **Ingest** | `PUT /apps/{appId}/wiki/documents/{documentId}` — idempotent upsert by content hash; batch via `POST .../documents/batch` (storage-only; no LLM on the write path) |
| **Digests** | `POST /apps/{appId}/wiki/digests/rebuild` — LLM digests (`Keywords:` + short bullets) into `summary`; refreshes the `wiki:catalog` document. Use after bulk ingest or with `force: true` to regenerate |
| **List / delete** | `GET` / `DELETE` under `/apps/{appId}/wiki/documents...` |
| **Query** | `POST /apps/{appId}/wiki/query` — keyword/FTS search returning a compact Markdown pack of top matches (supports `asOf` for point-in-time facts) |
| **Revisions / audit** | `GET .../documents/{id}/revisions` timeline; `GET .../wiki/audit?from=&to=` export |
| **Chat tools** | `wiki_search` (FTS/lexical hydrate) and `wiki_grep` (regex over Content/Summary) when `GlobalWikiEnabled` is true; optional `asOf` |
| **Config** | Toggle / budget via app runtime config (`GlobalWikiEnabled`, max chars); mid-turn budget via `MaxContextTokens` |

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

How that connects to chat: see **[Retrieval + agentic loop](#retrieval--agentic-loop)** — discovery tools in the loop, **not** classic RAG / embeddings.

### Retrieval + agentic loop

Global Wiki retrieval and the agentic gateway are **one pipeline**, not two products bolted together.

| Layer | What happens |
|---|---|
| **Session memory** | Always: recent history + compiled per-session wiki (+ rolling summary when present). |
| **Global digests** | Static inject: top-K digests from FTS on `Summary` (not full bodies). |
| **Global hydrate** | On demand: `wiki_search` / `wiki_grep` → compact Markdown (large results → artifacts). |
| **Other tools** | Optional: shell/python/node (sandbox/ACA), MCP (`server__tool`), web search. |
| **Skills / rules / hooks** | Shape the loop; validators / PreToolUse can deny or require confirm. |

**Dynamic context discovery (Cursor-style, DB-backed)**

Theory: minimize tokens in the big chat model. Prefer **lazy discovery** over stuffing context. **No embeddings / vector RAG** in this model — retrieval is digests + FTS/lexical + regex + tools.

| Mechanism | Role |
|---|---|
| **Static (every turn)** | Persona + budgeted session wiki + rolling summary + top-K Global Wiki digests |
| **On demand** | `wiki_search`, `wiki_grep`, `skill_search`/`skill_read`, `rule_search`/`rule_read`, `tool_describe`, artifacts |
| **Models** | `LlmModel` (answer/agent) + `WikiLlmModel` (digests, rolling summary, mid-turn compaction) |
| **Mid-turn compaction** | If estimated tokens &gt; `MaxContextTokens`: archive transcript as `history:…` artifact, summarize with `WikiLlmModel`, shrink messages; emit phase `Compacting` |
| **Artifacts** | Long tool/MCP outputs stored per session; sandbox/terminal **always** archived with a short preview + `artifactId`. Tools: `artifact_tail` / `artifact_read` |
| **Lazy tools[]** | Built-ins and MCP expose name + one-line description + open schema; call `tool_describe` before first use of an unfamiliar tool |
| **Lazy skills** | Prompt lists up to 3 default skill ids; `skill_search` → snippets; `skill_read` → body |
| **Rules** | `always_on` injected into system; `requestable` via `rule_search` / `rule_read` (`Activation` on catalog skills) |
| **Hooks** | Guardrail kinds `pre-tool-use` / `post-tool-use` (deny/allow patterns, require confirm, redact) |
| **Subagents** | `delegate_task` spawns depth-1 child session; result returns as summary + artifact |
| **Todos** | `todo_write` updates session todos for Admin Chat Lab |
| **Telemetry** | `context_memory.discovery` — static vs discovery chars, tool observations, compaction count, llm calls (+ Prometheus) |

**Built-in discovery / wiki tools (agentic)**

| Tool | Purpose |
|---|---|
| `wiki_search` | FTS/lexical hydrate of Global Wiki documents |
| `wiki_grep` | Regex over Content/Summary (budgeted hits) |
| `artifact_read` / `artifact_tail` | Recover truncated / archived tool output |
| `skill_search` / `skill_read` | Find and load skill bodies |
| `rule_search` / `rule_read` | Requestable rules |
| `tool_describe` | Full tool schema/description |
| `session_log_search` | Grep session log when summary misses a detail |
| `delegate_task` | Spawn isolated subagent (depth 1) |
| `todo_write` | Session todo list for the Admin timeline |
| `fetch_url` / `http_request` | Allowlisted HTTP GET/verbs (`agentic.tools.http`, fail-closed allowlist + SSRF checks) |
| `web_search` | On-demand web search tool (Tavily/Brave; independent of pre-chat WebSearch enrichment) |
| `read_image` / `screenshot_describe` | Vision attach (only when model `SupportsVision` + `tools.vision.enabled`) |
| `browser_*` | Playwright navigate/snapshot/click/type/screenshot via sandbox `/browser` |
| `parse_pdf` / `read_document` | PDF text extract from session artifacts (`tools.documents`) |
| `canvas_write` / `canvas_read` | Session Canvas JSON for Admin Chat Lab panel |

**When the agentic loop runs**

```text
AgenticEnabled =
    agentic.enabled && HasAnyTools
    (execution | MCP | http | vision | browser | documents | canvas)
```

Global Wiki **does not** force the agentic loop. Digests are injected in the enrich step; enable agentic when you need tools (sandbox/MCP/`wiki_search` hydrate / discovery helpers / HTTP).

**End-to-end flow**

```text
1. Ingest     PUT/batch  /apps/{id}/wiki/documents…     (storage only)
2. Digests    POST       /apps/{id}/wiki/digests/rebuild (WikiLlmModel + wiki:catalog)
3. Chat       POST       /v1/chat/completions
              │
              ├─ build prompt (persona + rolling summary + session wiki + digests + history)
              ├─ if AgenticEnabled: tools wiki_* + discovery + sandbox/MCP (lazy schemas)
              ├─ loop: optional Compacting → LLM ↔ tool_calls ↔ short observations
              │         ↔ Pre/PostToolUse hooks ↔ validation / HITL
              └─ return OpenAI choices[] + context_memory.agentic / .discovery
```

**Same request, grounded + action**

A single user turn can:

1. Call `wiki_search` / `wiki_grep` for ticket/policy facts  
2. Call an MCP tool (e.g. Zuora) or sandbox code with that evidence  
3. Pass guardrails / hooks / HITL if configured  
4. Answer from tool output only  

Clients keep sending a normal chat body; they do not implement retrieval or orchestration.

**Configure the combo (Admin)**

1. **Config → Global Wiki** — enabled + tool char budget  
2. **Config → Agentic** — enable for sandbox/MCP; skills/rules; prompt profile  
3. Ingest docs + `digests/rebuild` for large corpora  
4. Smoke in **Chat Lab** — timeline shows Compacting / tools / subagents; side panels for Todos, Artifacts, wiki refs  

**Not the same as**

| | Session wiki | Global digests (inject) | `wiki_search` / `wiki_grep` | Classic RAG |
|---|---|---|---|---|
| Scope | One user session | Whole `appId` | Whole `appId` | N/A |
| In prompt every turn? | Yes (budgeted) | Yes (top-K digests) | No — tool on demand | N/A |
| Needs agentic loop? | No | No | Yes (when tools on) | N/A |
| Embeddings? | No | No | No (FTS / regex) | Usually yes |

### Agentic Gateway

- **Same endpoint** — preferred `POST /v1/chat/completions` (legacy `POST /api/chat`). Loop runs when `AgenticEnabled` is true: `agentic.enabled && tools` — see [Retrieval + agentic loop](#retrieval--agentic-loop).
- **Orchestrator** — loop with iteration cap, configurable timeout, mid-turn compaction, and validation before returning the final answer. Built-in `wiki_search` / `wiki_grep` + discovery tools when the loop is active.
- **Skills & guardrail packs (two levels)** — platform catalog (File or Postgres), seeded on startup, plus optional per-app inventory.
  - **Platform** — Markdown skills + guardrail packs with `IsDefaultEnabled`; apply to **all** apps. Admin **Skills** (`/skills`) or `/admin/agentic/skills|guardrails`.
  - **Activation** — `skill` (default), `always_on` (injected), `requestable` (via `rule_*` tools).
  - **Per app** — CRUD under `/apps/{appId}/policies` or `/admin/apps/{appId}/skills|guardrails`. `IsEnabled` toggles; additive to platform defaults (apps cannot turn off platform packs).
  - **Runtime** — union of platform default-on + app enabled. Skill `sandbox-facts-selfhosted` only when a self-hosted sandbox tool is configured.
  - Legacy `agentic.policyPacks` is ignored.
  - Distinct from loop **`guardrails`** below (`maxIterations`, HITL keywords, egress, validation mode) — those remain numeric/policy knobs on the orchestrator.
  - Hook kinds: `pre-tool-use`, `post-tool-use` (JSON config: match/deny/allow patterns, require confirm, redact).
- **Execution tools**
  - ACA Dynamic Sessions: `shell_execute`, `python_execute`, `node_execute`, `container_execute` (custom image)
  - Self-hosted sandbox (`self-hosted-sandbox`): same tool names against **sandbox-runtime** (Compose) or your own gVisor/sandbox endpoint
  - Sandbox/terminal observations are **always** persisted as session artifacts (short preview in the loop).
- **Integration tools (MCP)**
  - Per-app MCP catalog with **HTTP** and **stdio** transports (`mcp-runtime` hosts stdio packages under `/opt/mcps`)
  - Qualified naming `server__tool`; rebuild with `POST /apps/{appId}/mcp/catalog/rebuild`
  - Lazy schemas in `tools[]`; `tool_describe` for full input schema
  - Credentials via `POST /apps/{appId}/mcp/credentials/{name}` (`Env` map for `AZURE_*` / `GITHUB_TOKEN`); test with `POST .../mcp/test/{name}`
  - **Cursor IDE MCP ids are not inbound** — see [inbound-mcp-guide.md](inbound-mcp-guide.md)
  - Sandbox fallback: same credential `Env` is injected into `shell_execute` / `python_execute` / `node_execute` for integrations named `azure-monitor`, `github`, or `git`
  - `mcp_servers` pass-through for backends that support it natively
  - Authentication: bearer, api-key, or **per-tenant OAuth client-credentials**
- **Subagents** — `delegate_task` runs a depth-1 child session with an isolated working set; returns summary + `artifactId`.
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
- **Prompt profiles** — `auto`, `ollama`, `openai`, `claude`, `qwen`, `composer` / `composer-like` for system snippets and tool-calling hints.
- **Harness mode** — `auto` | `weak` | `strong` (`agentic.harnessMode`). Weak inlines evidence rules and sanitizes schemas aggressively; Strong keeps lazy `skill_read` and prefers native tool_calls. Auto uses profile + model-size hints (`bonsai*` → Qwen/Weak).
- **Guardrail `live-data-evidence`** — rejects live-data answers (accounts/invoices/…) without a successful MCP/wiki tool step.
- **Format gate** — `llmOptions.format=json` is cleared on agentic iterations that send tools (conflicts with `tool_calls` / `response_format`).
- **Ollama `num_ctx`** — when `llmBackend=ollama` and `llmOptions.numCtx` is set, the gateway uses **`ollama-native`** (`/api/chat`) because Ollama `/v1` ignores `options.num_ctx`.
- **Qwen/Bonsai chat templates** — compaction merges into a single `system` message; the loop ensures a real `user` message exists (strict Jinja `raise_exception` packs still need a TEMPLATE patch).

#### Profile → capabilities (matrix)

| Profile / signal | Default harness | Sanitize schemas | Inline evidence | MCP tools cap hint |
|---|---|---|---|---|
| Qwen / Ollama / `bonsai*` | Weak | aggressive | yes | tenant `maxMcpToolsPerTurn` |
| OpenAI / Claude / Composer | Strong | minimal | no (lazy skills) | config |
| Override `harnessMode` | wins | follows mode | follows mode | follows mode |

Discovery telemetry also reports `harness_mode`, `resolved_prompt_profile`, `promoted_prose_tool_calls`, `schema_repair_level`.

### Streaming and latency

- With `stream: true`, tool calls and internal observations **do not leak** into the text stream.
- The final answer is emitted in chunks after the loop completes (or times out).
- Agentic progress metadata via `context_memory.agentic` (phases include `Compacting`, `SubagentStarted` / `SubagentCompleted`, tool start/end).
- Discovery counters via `context_memory.discovery` (static vs discovery chars, compaction, llm calls).
- Timeout with graceful partial response + header `X-Context-Memory-Agentic-Timed-Out`.

### Web search

- Providers: **Tavily**, **Brave**
- Modes: `heuristic`, `llm`, `always`, `off`
- Ephemeral context in the prompt; optional persistence of facts to the wiki
- Response headers: `X-Web-Search-Used`, `X-Web-Search-Provider`, etc.

### Multi-tenant and operations

- **Isolated apps** — API key, config, tools, and guardrails per `appId`
- **Rate limiting** — RPM/TPM per app and per user; extra weight for agentic turns
- **Telemetry** — requests, tokens, latency, wiki, web search, agentic discovery ratios, active users
- **Admin** — Blazor console at `ContextMemory.Admin.Web` (`:5200`); see [Admin UI guide](admin-ui.md)
- **Persistence**
  - `File` — development and single-node
  - `Postgres` — apps, profiles, **sessions/wiki**, artifacts, and HITL state in JSONB (multi-instance HA)

### Supported LLM backends

| Backend value | Wire protocol | Host default URL |
|---|---|---|
| `ollama` (default) | OpenAI `/v1` on `OllamaEndpoint` — **auto-switches to native when `numCtx` is set** | `ContextMemory:OllamaEndpoint` |
| `vllm` / `openai-compatible` / `openai` / `custom` | OpenAI `/v1/chat/completions` | `ContextMemory:OpenAiEndpoint` or per-app `llmEndpoint` |
| `lmstudio` | OpenAI `/v1` | `ContextMemory:LmStudioEndpoint` |
| `ollama-native` | Ollama `/api/chat` (respects `options.num_ctx`) | `ContextMemory:OllamaEndpoint` |

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
        "tool-calling-discipline"
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
| Skill | `small-model-abstention` | ❌ | Opt-in: prefer abstention over invented numbers/dates on weak models |
| Skill | `step-by-step-reasoning-brief` | ✅ | Short plan before tools on complex tasks |
| Rule | `rule-always-evidence` | ✅ | `always_on` — prefer tools/wiki over speculation |
| Rule | `rule-requestable-style` | ✅ | `requestable` — ultra-terse answers via `rule_read` |
| Guardrail | `url-fetch-required` | ✅ | Reject URL descriptions without fetch evidence |
| Guardrail | `sandbox-claim-reject` | ✅ | Reject false ACA/no-network claims |
| Guardrail | `require-error-disclosure` | ✅ | Reject answers that ignore failed tool steps |
| Guardrail | `block-credential-leak` | ✅ | Reject secret-like substrings in the answer |
| Guardrail | `pre-tool-deny-rm-rf` | ❌ | Sample `pre-tool-use` hook (opt-in) |
| Guardrail | `post-tool-redact-secrets` | ✅ | `post-tool-use` redact of secret-like patterns |
| Guardrail | `numeric-grounding` | ❌ | Opt-in: reject prices/dates/%/counts not in tool evidence (see [small-model-guide](small-model-guide.md)) |

Custom skills (non-system) can be created, imported, and exported; system seeds are updated in place on catalog seed but remain selectable per app.
---

