<p align="center">
  <a href="https://github.com/Kortexio/ContextMemory">
    <img src="docs/images/banner-sm.svg" width="800" alt="Kortexio ContextMemory — Memory you can open as a wiki">
  </a>
</p>

<p align="center">
  <a href="https://kortexio.io"><strong>Get Cloud key</strong></a>
  ·
  <a href="#quickstart-5-minutes">Self-host</a>
  ·
  <a href="docs/README.md">Docs</a>
  ·
  <a href="docs/aha-demo.html">Demo</a>
</p>

<p align="center">
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-AGPL%203.0-blue.svg" alt="License: AGPL-3.0"></a>
  <a href="https://dotnet.microsoft.com/"><img src="https://img.shields.io/badge/.NET-9.0-512BD4" alt=".NET 9"></a>
  <a href="https://github.com/users/Kortexio/packages/container/package/contextmemory"><img src="https://img.shields.io/badge/ghcr.io-contextmemory-blue?logo=docker" alt="Docker GHCR"></a>
  <a href="https://github.com/Kortexio/ContextMemory/actions/workflows/docker-publish.yml"><img src="https://github.com/Kortexio/ContextMemory/actions/workflows/docker-publish.yml/badge.svg" alt="Docker CI"></a>
  <a href="https://github.com/Kortexio/ContextMemory/actions/workflows/dotnet-tests.yml"><img src="https://img.shields.io/github/actions/workflow/status/Kortexio/ContextMemory/dotnet-tests.yml?branch=main&label=tests" alt="Tests"></a>
  <a href="https://github.com/Kortexio/ContextMemory/commits/main"><img src="https://img.shields.io/github/commit-activity/m/Kortexio/ContextMemory?style=flat-square" alt="GitHub commit activity"></a>
</p>

<p align="center">
  <strong>Your agent forgets. Fix that with memory you can open like a wiki.</strong>
</p>

<p align="center">
  One OpenAI-compatible <code>/v1</code> URL: wiki memory, agentic tool loop, skills/guardrails, MCP, sandbox, and HITL —
  self-hosted or <a href="https://kortexio.io">Cloud</a>. Not a vector black box. Not classic RAG inject.
</p>

---

## What is ContextMemory?

[ContextMemory](https://github.com/Kortexio/ContextMemory) is the open-source **agentic memory gateway** behind [Kortexio](https://kortexio.io).

Your app (or Cursor/Claude) keeps talking to a normal chat API. The gateway:

1. Authenticates the tenant and attaches **session wiki + history**
2. Runs an **agentic tool loop** when tools are enabled (wiki search, sandbox, MCP, …)
3. Applies **skills, guardrails, validators**, and optional **HITL** before destructive actions
4. Returns a standard OpenAI-shaped `chat.completions` response (streaming supported)

```
Your client (OpenAI SDK / Cursor MCP / curl)
        │
        ▼  POST /v1/chat/completions
┌────────────────────────────────────────────┐
│  ContextMemory (.NET 9)                    │
│  Auth · session wiki · Global Wiki tool    │
│  Agentic loop · skills · guardrails · HITL │
│  LLM backend (per app, OpenAI-compatible)  │
└───────┬──────────────────┬─────────────────┘
        ▼                  ▼
 sandbox-runtime      mcp-runtime / MCP servers
 (shell/python/node)  (HTTP + stdio, OAuth)
 or Azure ACA sessions
```

**Honest boundaries:** this is a **gateway + server-side harness**, not a client agent framework (LangGraph/CrewAI) and not an agent OS (Letta). You keep your OpenAI client; the loop runs on the server.

How we compare (Mem0 / Zep / Letta / **why we are not RAG**): [`docs/compare.md`](docs/compare.md).

---

## What it gives developers

| You need… | ContextMemory provides… |
|---|---|
| Memory that survives turns without rewriting your client | Session markdown wiki + history inject; send only the new message |
| Memory you can open, edit, audit | Files on disk / Postgres — not opaque embeddings |
| Shared company/docs knowledge in chat | **Global Wiki** digests + on-demand `wiki_search` / `wiki_grep` (**not** classic RAG / embeddings) |
| Point-in-time facts | Temporal revisions (`asOf` / supersede) on Global Wiki |
| Tools without a second orchestrator | Same `/v1`: sandbox + MCP integrations + wiki tools |
| Safer agents | Skills & guardrail packs, validators, confirmation keywords, HITL `[CONFIRM:id]` |
| Cursor / Claude permanent memory fast | MCP wedge: `memory_save` / `memory_search` / `memory_get` (+ `wiki_search`, `session_recall`) |
| Any LLM per tenant | Ollama, vLLM, LM Studio, OpenAI, Azure-compatible `/v1`, custom |
| Operate without writing a test client | **Admin** + **Playground** (timeline, todos, artifacts, HITL) |
| Full control on your infra | Docker/Compose self-host (API + Admin + mcp-runtime + sandbox) |
| Zero ops | [Kortexio Cloud](https://kortexio.io) (`cmk_live_…`) |

---

## Capabilities (full surface)

### Memory

- **Session wiki** — markdown pages, index, execution log; compaction; update every N turns; optional dedicated maintainer model; **rolling summary** in the system prompt
- **History** — last N messages (per-app budget); mid-turn **compaction** archives long transcripts as artifacts when over `MaxContextTokens`
- **Persona & rules** — `basePersona`, `businessRules`, `formatRules`, `wikiSchema` per app
- **Global Wiki** — app-scoped docs; ingest/batch APIs; digests; FTS; tools `wiki_search` / `wiki_grep`; revisions / audit / `asOf`
- **No vector RAG** — discovery is digests + lexical/FTS + tools (Cursor-style), not embeddings
- **Web search** (optional) — enrich turns; can persist into wiki

### Agentic harness (server-side)

When agentic tools are enabled, the gateway runs a tool loop:

- **Iterations / timeout** — max steps, loop timeout, partial answer on timeout; mid-turn compaction phase
- **Built-in tools** — `wiki_search`, `wiki_grep`; sandbox `shell_execute` / `python_execute` / `node_execute` / `container_execute` (self-hosted or ACA); discovery helpers (`artifact_*`, `skill_*`, `rule_*`, `tool_describe`, `session_log_search`, `delegate_task`, `todo_write`)
- **Lazy tool schemas** — MCP and built-ins listed with short/open schemas; `tool_describe` for full args
- **Artifacts** — long outputs (and all sandbox runs) stored per session; loop keeps a short preview + `artifactId`
- **Subagents** — `delegate_task` (depth 1, isolated child session)
- **MCP tools** — per-app catalog (`server__tool`), allow/deny, max tools per turn, OAuth/credentials
- **Validation modes** — `deterministic` · `hybrid` · `llm-judge`
- **Hooks** — PreToolUse / PostToolUse guardrail kinds
- **HITL** — pause before destructive tools; `[CONFIRM:id]` / cancel; checkpoint in session log
- **Progress** — `context_memory.agentic` phases (incl. Compacting / Subagent*) + `context_memory.discovery` counters
- **Prompt profiles** — `auto` / `ollama` / `openai` / `claude` / `qwen` / `composer`
- **Network egress policy** — restricted/allowed + host allowlists

### Skills & guardrails

- **Platform catalog** — shared skills/guardrails (Admin → Skills); import `.skill.json` / `.guardrail.json`
- **Activation** — `skill` | `always_on` | `requestable` (rules loaded via `rule_search` / `rule_read`)
- **Per-app policies** — additive inventory on top of platform defaults
- Seeded examples include anti-hallucination, tool-calling discipline, wiki-first-for-docs, privacy/secrets, transparent failures, and more
- Guardrail kinds include URL fetch, sandbox claims, tool-failure disclosure, blocked patterns, pre/post tool-use hooks

### MCP (two directions)

| Direction | Role |
|---|---|
| **Outbound wedge** | Cursor/Claude → ContextMemory (`mcp-server/`) for memory tools |
| **Inbound catalog** | ContextMemory agent → your MCP servers (HTTP/stdio via `mcp-runtime`, OAuth, catalog rebuild) |

### Admin console

Blazor Admin (`:5200`, Master Key auth) — operators configure tenants without touching JSON by hand:

| Area | What you configure / do |
|---|---|
| Dashboard | Apps, requests, wiki/web-search stats |
| New app / credentials | Register tenant, mint/rotate `cm_live_…` |
| **Playground** | Chat Lab: agentic timeline (Compacting/Subagent), Todos, Artifacts, wiki refs, HITL |
| LLM | Backend, model, endpoint, API key, history, streaming, think |
| Memory & wiki | Session budgets, compaction, maintainer model, Global Wiki on/off + char budget |
| Web search | Provider, mode, persist-to-wiki |
| Rate limits | RPM/TPM (+ agentic weight) |
| Persona & rules | Persona, business/format rules, wiki schema |
| Agentic | Full gateway knobs: tools, MCP, sandbox, validators, HITL, egress |
| Skills & policies | Platform + per-app skills/guardrails |
| Settings | API base URL, Master Key, health |

<p align="center">
  <img src="docs/images/admin-dashboard.png" width="800" alt="ContextMemory Admin dashboard">
</p>

<p align="center">
  <img src="docs/images/admin-llm-backend.png" width="390" alt="LLM backend picker">
  &nbsp;
  <img src="docs/images/admin-agentic.png" width="390" alt="Agentic gateway config">
</p>

<p align="center">
  <img src="docs/images/admin-playground.png" width="390" alt="Admin Playground">
  &nbsp;
  <img src="docs/images/admin-skills.png" width="390" alt="Skills and guardrails">
</p>

Guide: [`docs/admin-ui.md`](docs/admin-ui.md) · HITL: [`docs/hitl.md`](docs/hitl.md)

### Self-host stack

Docker Compose brings up a full local platform:

| Service | Role |
|---|---|
| **API** (`:5100`) | Gateway `/v1`, admin APIs, metrics |
| **Admin** (`:5200`) | Operator UI |
| **mcp-runtime** | Stdio/HTTP MCP sidecar |
| **sandbox-runtime** | Isolated shell/python/node execution |

Persistence: **File** (single-node) or **Postgres** (HA + FTS). Images: `ghcr.io/kortexio/contextmemory` · `ghcr.io/kortexio/contextmemory-admin`.

### Observability

Prometheus `/metrics` · OpenTelemetry (Aspire) · per-app telemetry in Admin.

---

## Quickstart (5 minutes)

### 1. Start the gateway

Default demo points at Ollama on the host. Swap the backend anytime in **Admin → Config → LLM** or `PATCH /admin/apps/{id}/config`.

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

Full stack (API + Admin + MCP + sandbox): see [`docs/self-host.md`](docs/self-host.md) / `docker-compose.yml`.

Admin UI: typically `http://localhost:5200`.

No Docker? Use **[Kortexio Cloud](https://kortexio.io)** (`cmk_live_…`) and set `CONTEXTMEMORY_BASE_URL` to the cloud API.

### 2. Wire MCP into Cursor

```bash
git clone https://github.com/Kortexio/ContextMemory.git
cd ContextMemory/mcp-server && npm install && node print-mcp-config.mjs
```

Paste into **Cursor → Settings → MCP** (or `~/.cursor/mcp.json`). Same snippet works for Claude Desktop. Details: [`mcp-server/README.md`](mcp-server/README.md).

### 3. Aha (memory wedge)

| Chat | You say | Agent should |
|---|---|---|
| **A** | `Remember: staging DB is postgres-staging-01` | `memory_save` |
| **B** (new) | `What is our staging DB?` | `memory_search` + answer |

CLI: `./scripts/aha-demo.sh` or `.\scripts\aha-demo.ps1` · storyboard: [`docs/aha-demo.html`](docs/aha-demo.html)

### Cloud vs self-host

| | **[Kortexio Cloud](https://kortexio.io)** | **Self-host (this repo)** |
|---|---|---|
| Best for | Zero ops | Full control (API + Admin + MCP + sandbox) |
| Key | `cmk_live_…` (no `X-App-Id`) | `cm_live_…` + `X-App-Id` |
| Chat body | Identical OpenAI `/v1` | Identical OpenAI `/v1` |

Guides: [Cloud](docs/cloud.md) · [Self-host](docs/self-host.md)

### Chat drop-in

```bash
curl -X POST http://localhost:5100/v1/chat/completions \
  -H "Content-Type: application/json" \
  -H "X-App-Id: demo-dev" -H "X-User-Id: user-42" -H "X-Session-Id: sess-abc" \
  -H "Authorization: Bearer cm_live_dev_key_change_me" \
  -d '{"model":"qwen3.5:9b","messages":[{"role":"user","content":"Hello"}]}'
```

Thin header helpers (**not** full SDKs): [`@kortexio/contextmemory`](https://www.npmjs.com/package/@kortexio/contextmemory) · [`kortexio-contextmemory`](https://pypi.org/project/kortexio-contextmemory/)

---

## Documentation & support

| Doc | Topic |
|---|---|
| [docs/compare.md](docs/compare.md) | Why it exists · vs Mem0 / Zep / Letta · **why we are not RAG** |
| [docs/architecture-and-features.md](docs/architecture-and-features.md) | Wiki, temporal facts, agentic, skills |
| [docs/admin-ui.md](docs/admin-ui.md) | Admin UI map |
| [docs/hitl.md](docs/hitl.md) | Human-in-the-loop |
| [docs/api.md](docs/api.md) | HTTP API |
| [docs/cloud.md](docs/cloud.md) · [docs/self-host.md](docs/self-host.md) | Cloud · Docker / Compose |
| [docs/ops.md](docs/ops.md) | Ops & troubleshooting |
| [docs/README.md](docs/README.md) | Full docs index |

Website: [kortexio.io](https://kortexio.io) · Email: [hello@kortexio.io](mailto:hello@kortexio.io)

---

## License

**AGPL-3.0** for this open-source core. Commercial / hosted offerings: [kortexio.io](https://kortexio.io). See [docs/license-and-support.md](docs/license-and-support.md).
