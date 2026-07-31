# ContextMemory Agentic Gateway

[![License: AGPL-3.0](https://img.shields.io/badge/License-AGPL%203.0-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-9.0-512BD4)](https://dotnet.microsoft.com/)
[![Docker](https://img.shields.io/badge/ghcr.io-contextmemory-blue?logo=docker)](https://github.com/users/Kortexio/packages/container/package/contextmemory)
[![CI](https://github.com/Kortexio/ContextMemory/actions/workflows/docker-publish.yml/badge.svg)](https://github.com/Kortexio/ContextMemory/actions/workflows/docker-publish.yml)

**Give an LLM memory by swapping a URL. That same URL can also act when it needs to.**

ContextMemory is a context and agent proxy for applications that talk to LLMs. The **preferred public wire format is OpenAI-compatible**: `POST /v1/chat/completions` (response with `choices[]`) and `GET /v1/models`. The same OpenAI-compatible protocol is used **downstream** to talk to Ollama (`/v1`), vLLM, LM Studio, OpenAI, or any compatible server.

Legacy Ollama-native routes `POST /api/chat` and `POST /api/generate` remain available (deprecated) for older clients.

Behind the scenes, the gateway enriches each turn with session memory (a per-session markdown wiki), optional **Global Wiki** retrieval via the `wiki_search` tool, optional web search, and — when enabled — an **agentic loop** with tools isolated per tenant.

**Contents:** [Why](#why-it-exists) · [Cloud vs self-host](#two-ways-to-run-it) · [Cloud quick start](#quick-start--kortexio-cloud) · [Self-host / Docker](#quick-start--self-host) · [Architecture](#architecture-in-30-seconds) · [Features](#features) · [API](#api--stable-contract) · [Troubleshooting](#troubleshooting) · [License](#licensing)

---

> ### Don't want to self-host?
> **[Kortexio Cloud](https://kortexio.io)** is the hosted version of this gateway — same request body and response schema, zero infrastructure, **bring your own LLM key** (no markup on tokens). Get an API key and point your chat endpoint at it in minutes. **[Start free →](https://kortexio.io)**
>
> Self-hosting this open-source core and running on Kortexio Cloud share the **same OpenAI-compatible chat body** (`/v1/chat/completions`). The only differences are the API key prefix and whether you send `X-App-Id`. Prototype locally, move to the cloud without rewriting your chat payload — or the other way around.

---

## Why it exists

| Problem | ContextMemory solution |
|---|---|
| The LLM forgets context between messages | Per-session compiled wiki + recent history injected automatically |
| Product/ops docs live outside the chat session | **Global Wiki** — app-scoped knowledge base searched on demand via `wiki_search` |
| You need actions (shell, APIs, MCP) without a new endpoint | Agentic loop on the same `/v1/chat/completions`, invisible to the client |
| Each client/tenant needs different tools and rules | Per-app configuration: ACA, self-hosted sandbox, MCP, guardrails, prompts |
| Destructive actions need human control | Blocking human-in-the-loop with wiki checkpoints |
| Streaming with multi-step loops is complex | Internal buffer: client only receives final text; optional progress via metadata |

---

## Two ways to run it

| | **Kortexio Cloud** (hosted) | **Self-host** (this repo) |
|---|---|---|
| Infrastructure | None — managed for you | You run the .NET 9 gateway |
| LLM | BYOK via dashboard — set provider + model + key | You point the gateway at your own backend in `appsettings` |
| API key format | `cmk_live_...` | `cm_live_...` |
| Tenant selection | Bound to your key — no `X-App-Id` | `X-App-Id` header per app |
| Request body & response | **Identical** (OpenAI `/v1`) | **Identical** (OpenAI `/v1`) |
| Best for | Shipping fast, no ops | Full control, air-gapped / on-prem |
| Get started | [kortexio.io](https://kortexio.io) | [Quick start ↓](#quick-start--self-host) |

Both speak the **same `POST /v1/chat/completions`** contract. The only auth differences are the key prefix and whether you pass `X-App-Id`. Response is OpenAI-style `choices[]`.

---

## Quick start — Kortexio Cloud

The fastest path: no build, no database, no Ollama to run.

### 1. Get a key and connect your LLM (BYOK)

Create a free account at **[kortexio.io](https://kortexio.io)** and copy your API key (starts with `cmk_live_`).

Kortexio Cloud is **bring-your-own-key**: Kortexio orchestrates memory and agentic — text generation always uses your provider. In your app's **LLM provider** tab on the dashboard, pick a provider (OpenAI, Azure OpenAI, Anthropic, your own Ollama, …), set the model id, and paste your own provider key — **no markup on tokens**. Use **Test connection** to verify it before you ship. The `model` you send in each request must match the one configured there.

### 2. Point your endpoint here

If you already call an OpenAI-compatible `POST /v1/chat/completions`, change one URL and keep the same body and response parsing:

```diff
- POST http://localhost:11434/v1/chat/completions
+ POST https://api.kortexio.io/v1/chat/completions
```

Coming from Ollama native `/api/chat`? Switch to `/v1/chat/completions` and parse `choices[0].message.content`.

### 3. First chat request

```bash
# Turn 1 — teach it something
curl -X POST https://api.kortexio.io/v1/chat/completions \
  -H "Content-Type: application/json" \
  -H "X-User-Id: user-42" \
  -H "X-Session-Id: sess-abc" \
  -H "Authorization: Bearer cmk_live_..." \
  -d '{
    "model": "gpt-4o-mini",
    "messages": [{ "role": "user", "content": "Remember: KORTEX-PINEAPPLE" }]
  }'
```

```bash
# Turn 2 — same X-Session-Id, memory recalled automatically
curl -X POST https://api.kortexio.io/v1/chat/completions \
  -H "Content-Type: application/json" \
  -H "X-User-Id: user-42" \
  -H "X-Session-Id: sess-abc" \
  -H "Authorization: Bearer cmk_live_..." \
  -d '{
    "model": "gpt-4o-mini",
    "messages": [{ "role": "user", "content": "What was the secret word?" }]
  }'
```

**Response (OpenAI schema — same as self-host):**

```json
{
  "id": "chatcmpl-...",
  "object": "chat.completion",
  "model": "gpt-4o-mini",
  "choices": [{
    "index": 0,
    "message": { "role": "assistant", "content": "The secret word is KORTEX-PINEAPPLE." },
    "finish_reason": "stop"
  }]
}
```

That's the entire integration. Session memory works on the next turn automatically — no embeddings, no vector DB, no retrieval logic to write.

**Required headers:** `X-User-Id`, `Authorization: Bearer cmk_live_...`  
**Optional:** `X-Session-Id` (generated by the API if omitted). Your tenant is inferred from the key — you do **not** send `X-App-Id` on Cloud.  
**`model`:** required in the body, and it must match the provider/model you configured in the **LLM provider** tab (BYOK).

---

## Quick start — self-host

Run the open-source gateway yourself — the path for **on-prem or fully local** deployments. Unlike Cloud (where the dashboard wires up your LLM for you), here **you point the gateway at your own LLM backend** — endpoint and model — in config. Same chat body, same Ollama response as Cloud; you supply the `X-App-Id` and use a `cm_live_` key.

### Fastest: one-liner from GHCR (API)

Public images (published on every push to `main`):

| Image | Package |
|---|---|
| `ghcr.io/kortexio/contextmemory` | [API](https://github.com/users/Kortexio/packages/container/package/contextmemory) |
| `ghcr.io/kortexio/contextmemory-admin` | [Admin UI](https://github.com/users/Kortexio/packages/container/package/contextmemory-admin) |

**API only** (needs [Docker](https://docs.docker.com/get-docker/) + Ollama on the host):

```bash
docker run --rm -p 5100:8080 \
  --add-host=host.docker.internal:host-gateway \
  -v contextmemory-data:/app/data \
  -e ContextMemory__OllamaEndpoint=http://host.docker.internal:11434 \
  -e ContextMemory__MasterKey=cm_master_dev_key_change_me \
  -e ContextMemory__Apps__demo-dev__ApiKey=cm_live_dev_key_change_me \
  -e ContextMemory__Apps__demo-dev__LlmModel=qwen3.5:9b \
  ghcr.io/kortexio/contextmemory:latest
```

Then: http://localhost:5100/health

**API + Admin from GHCR (no local build):**

```bash
docker compose -f docker-compose.ghcr.yml up -d
```

Or the helper scripts:

```bash
./scripts/docker-run.sh --with-admin          # pull from GHCR
./scripts/docker-run.sh --build --with-admin  # build locally instead
```

```powershell
.\scripts\docker-run.ps1 -WithAdmin
.\scripts\docker-run.ps1 -Build -WithAdmin
```

### Build from source: Docker Compose

Builds and starts the **API** (`:5100`) and **Admin** (`:5200`) locally. Requires [Docker](https://docs.docker.com/get-docker/) and an LLM reachable from the containers (Ollama on the host by default).

```bash
git clone https://github.com/Kortexio/ContextMemory.git
cd ContextMemory

# optional: customize ports / keys / model
cp .env.example .env

# build + run (File persistence — no Postgres required)
docker compose up --build
```

Then open **[http://localhost:5200](http://localhost:5200)** — Settings are pre-filled for the Compose network (`API base URL` = `http://api:8080`, demo Master Key). Click **Test connection**.

| Service | URL / value |
|---|---|
| API | http://localhost:5100 |
| Admin | http://localhost:5200 |
| Health | http://localhost:5100/health |
| Demo app | `X-App-Id: demo-dev` / Bearer `cm_live_dev_key_change_me` |
| Master key | `cm_master_dev_key_change_me` |

**Ollama on the host**

```bash
ollama pull qwen3.5:9b
# Compose default: ContextMemory__OllamaEndpoint=http://host.docker.internal:11434
```

**Useful Compose env vars** (see [`.env.example`](.env.example)):

| Variable | Default | Meaning |
|---|---|---|
| `API_PORT` / `ADMIN_PORT` | `5100` / `5200` | Host ports |
| `OLLAMA_ENDPOINT` | `http://host.docker.internal:11434` | LLM from inside the API container |
| `DEFAULT_LLM_MODEL` | `qwen3.5:9b` | Seed + default model |
| `MASTER_KEY` | `cm_master_dev_key_change_me` | Admin Master Key |
| `DEMO_APP_API_KEY` | `cm_live_dev_key_change_me` | Seed app API key |
| `PERSISTENCE_PROVIDER` | `File` | `File` or `Postgres` |

Stop with `Ctrl+C`, or `docker compose down`. Data for File mode lives in the `cm_data` Docker volume.

**Chat smoke test against the containerized API:**

```bash
curl -X POST http://localhost:5100/api/chat \
  -H "Content-Type: application/json" \
  -H "X-App-Id: demo-dev" \
  -H "X-User-Id: user-123" \
  -H "Authorization: Bearer cm_live_dev_key_change_me" \
  -d '{"model":"qwen3.5:9b","messages":[{"role":"user","content":"Hello"}]}'
```

### Prerequisites (dotnet run)

- .NET 9 SDK
- Ollama (or another configured backend) reachable on the network
- Optional: PostgreSQL 14+ for production / multi-instance HA

### 1. Configure

The committed `appsettings.json` uses **safe placeholders** and `PersistenceProvider: File` (no database required). Seed app id: `demo-dev` with key `cm_live_dev_key_change_me` (change before any real use).

**Local secrets** — choose one approach (never commit real values):

| Method | How |
|---|---|
| User Secrets (recommended) | `cd src/ContextMemory.Api` then `dotnet user-secrets set "ContextMemory:MasterKey" "your-key"` |
| Environment variables | See [`.env.example`](.env.example) for the `__` naming convention |
| Development file | Create `src/ContextMemory.Api/appsettings.Development.json` (gitignored) with local overrides |

For **PostgreSQL** in production or multi-instance HA:

```json
{
  "ConnectionStrings": {
    "ContextMemory": "Host=localhost;Port=5432;Database=contextmemory;Username=...;Password=..."
  },
  "ContextMemory": {
    "PersistenceProvider": "Postgres",
    "DataPath": "../../data",
    "OllamaEndpoint": "http://localhost:11434",
    "MasterKey": "your-master-key",
    "Apps": {
      "my-app": {
        "ApiKey": "cm_live_...",
        "SystemPrompt": "You are a helpful assistant.",
        "LlmModel": "qwen3.5:9b"
      }
    }
  }
}
```

Use `"Postgres"` exactly (not `Postgresql`). Relative `DataPath` values resolve from the API content root.

#### `appsettings.json` field reference

| Field | Meaning |
|---|---|
| `ConnectionStrings:ContextMemory` | PostgreSQL connection string when `PersistenceProvider` is `Postgres` |
| `ContextMemory:PersistenceProvider` | `File` (default) or `Postgres` |
| `ContextMemory:DataPath` | Root for file-based persistence (apps, sessions, wiki) |
| `ContextMemory:OllamaEndpoint` | Default Ollama (or Ollama-compatible) backend base URL |
| `ContextMemory:DefaultLlmModel` | Fallback model when an app has no `LlmModel` |
| `ContextMemory:MasterKey` | Secret for Admin API / admin dashboard |
| `ContextMemory:AdminCorsOrigins` | Allowed browser origins for Admin UI CORS |
| `ContextMemory:WebSearch:*` | Tavily/Brave keys, default provider, timeout |
| `ContextMemory:Apps` | Seed map of tenant apps; each key is the **app id** (`X-App-Id`) |

Per-app runtime settings (agentic tools, wiki schema, web-search toggles, guardrails) are managed via the admin API or Admin UI host, not only this seed section.

### 2. Start the API

```bash
cd src/ContextMemory.Api
dotnet run
```

API defaults to `http://localhost:5100` (Swagger in Development at `/swagger`).

### 3. First chat request

```bash
curl -X POST http://localhost:5100/api/chat \
  -H "Content-Type: application/json" \
  -H "X-App-Id: demo-dev" \
  -H "X-User-Id: user-123" \
  -H "X-Session-Id: sess-abc" \
  -H "Authorization: Bearer cm_live_dev_key_change_me" \
  -d '{
    "model": "qwen3.5:9b",
    "messages": [{ "role": "user", "content": "Hi, do you remember my name?" }]
  }'
```

**Response (Ollama schema — identical to Cloud):**

```json
{
  "model": "qwen3.5:9b",
  "message": { "role": "assistant", "content": "..." },
  "done": true,
  "context_memory": {
    "message_id": "...",
    "agentic": {
      "phase": "Completed",
      "awaiting_confirmation": false,
      "steps": [{ "tool_name": "shell_execute", "success": true }]
    }
  }
}
```

**Required headers:** `X-App-Id`, `X-User-Id`, `Authorization: Bearer {API_KEY}`  
**Optional:** `X-Session-Id` (generated by the API if omitted).

### 4. Admin UI and Chat Lab

Prefer Docker Compose above, or run the Admin host locally against a local API:

```bash
cd src/ContextMemory.Admin.Web
dotnet run
```

Open **[http://localhost:5200](http://localhost:5200)** → **Settings** → set API URL `http://localhost:5100` and your Master Key (`ContextMemory:MasterKey`) → **Test connection**. The Admin UI is fully in English with short help text under each field.

- **Applications** — register apps, stats, rotate API keys  
- **Apps → Config** — LLM, wiki, web search, rate limits, **Agentic Gateway**  
- **Chat Lab** — streaming, agentic timeline, human confirmation  

The API also serves a short HTML pointer at [http://localhost:5100/admin](http://localhost:5100/admin). Admin HTTP APIs still require the Master Key bearer token.

---

## Architecture in 30 seconds

```
Your app (Ollama-compatible client)
        │
        ▼ POST /api/chat
┌───────────────────────────────────────────┐
│  ContextMemory Gateway (.NET 9)           │
│  1. Auth + tenant (API key, X-App-Id)     │
│  2. Memory: history + session wiki        │
│  3. Global Wiki tool (wiki_search)        │
│  4. Web search (optional)                 │
│  5. Agentic loop (if enabled)             │
│     LLM ↔ tools ↔ validation ↔ HITL       │
│  6. Ollama-schema response                │
└───────────┬───────────────┬───────────────┘
            ▼               ▼
     ACA / self-hosted   MCP Servers
     sandbox (shell/     (Zuora, Jira, …)
     python/node/custom) JSON-RPC + OAuth
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
| **Ingest** | `PUT /apps/{appId}/wiki/documents/{documentId}` — idempotent upsert by content hash; batch via `POST .../documents/batch` |
| **List / delete** | `GET` / `DELETE` under `/apps/{appId}/wiki/documents...` |
| **Query** | `POST /apps/{appId}/wiki/query` — keyword search returning a compact Markdown snippet + scored matches |
| **Chat** | Tool `wiki_search` in the agentic loop when `GlobalWikiEnabled` is true (default) |
| **Config** | Toggle / budget via app runtime config (`GlobalWikiEnabled`, max chars) |

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

# Search without chat
curl -X POST http://localhost:5100/apps/demo-dev/wiki/query \
  -H "Content-Type: application/json" \
  -H "X-App-Id: demo-dev" \
  -H "Authorization: Bearer cm_live_dev_key_change_me" \
  -d '{"query":"subscription renewal invoice","topK":5}'
```

### Agentic Gateway

- **Same endpoint** — `POST /api/chat`; enabled via `agentic.enabled` in tenant config.
- **Orchestrator** — loop with iteration cap, configurable timeout, and validation before returning the final answer.
- **Execution tools**
  - ACA Dynamic Sessions: `shell_execute`, `python_execute`, `node_execute`, `container_execute` (custom image)
  - Self-hosted sandbox (`self-hosted-sandbox`): same tool names against your own gVisor/sandbox endpoint
- **Integration tools (MCP)**
  - Dynamic catalog per configured MCP server
  - Qualified naming `server__tool`
  - `mcp_servers` pass-through for backends that support it natively
  - Authentication: bearer, api-key, or **per-tenant OAuth client-credentials**
- **Per-tenant guardrails**
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
- **Admin** — Blazor console at `ContextMemory.Admin.Web` (`:5200`), HTTP admin API, HTML pointer at `/admin`
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
| `lmstudio` | OpenAI-compatible `/v1` | `ContextMemory:LmStudioEndpoint` |
| `openai` | OpenAI-compatible `/v1` | `ContextMemory:OpenAiEndpoint` + `OpenAiApiKey` |
| `openai-compatible` / `custom` | Same as `openai` | same (override per app) |

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

Configure via the Admin UI (`/apps/{appId}/config` on `:5200`) or `PATCH /admin/apps/{appId}/config` with the Master Key.

---

## Human-in-the-loop — how it works

1. The model proposes a tool that matches `requireConfirmationFor` (e.g. a command containing `delete`).
2. Execution **stops**; the API returns a confirmation prompt and header `X-Context-Memory-Agentic-Awaiting-Confirmation`.
3. The user replies with confirmation (e.g. `confirm`, `approve`, or `[CONFIRM:abc123]`).
4. The tool runs; the loop continues until a validated final answer.
5. If the iteration limit is reached, `humanReviewOnMaxIterations` requests **approval of the partial answer**.

Everything is recorded in the session `log.md` for audit.

---

## API — stable contract

| Endpoint | Description |
|---|---|
| `POST /v1/chat/completions` | **Preferred** OpenAI-compatible chat (+ agentic, session memory, Global Wiki tool, web search) |
| `GET /v1/models` | OpenAI-compatible model list for the tenant |
| `POST /api/chat` | Deprecated Ollama-compatible chat |
| `POST /api/generate` | Deprecated Ollama-compatible generate |
| `POST /api/generate` | Ollama-compatible completion |
| `PUT /apps/{id}/wiki/documents/{documentId}` | Upsert Global Wiki document |
| `POST /apps/{id}/wiki/documents/batch` | Batch upsert Global Wiki documents |
| `GET /apps/{id}/wiki/documents` | List Global Wiki documents |
| `DELETE /apps/{id}/wiki/documents/{documentId}` | Delete Global Wiki document |
| `POST /apps/{id}/wiki/query` | Keyword search over Global Wiki |
| `GET /apps/{id}/config` | Runtime config (auth with app API key) |
| `PATCH /admin/apps/{id}/config` | Update config (Master Key), including `GlobalWikiEnabled` |
| `GET /health` | API, Ollama, Postgres health |
| `GET /admin` | HTML pointer to the Admin UI host |

The preferred chat response is the **OpenAI schema** — `choices[0].message.content`. Legacy `/api/chat` still returns Ollama `message.content` / `done`.

---

## Persistence

| Component | File | Postgres |
|---|---|---|
| App registry + API keys | ✅ | ✅ |
| Runtime config (LLM, agentic, wiki) | ✅ | ✅ |
| Sessions, messages, session wiki | ✅ | ✅ |
| Global Wiki documents | ✅ | ✅ |
| Pending HITL state | ✅ | ✅ |
| Telemetry / rate limits | in-memory | in-memory |

EF Core migrations live in `ContextMemory.Infrastructure` (includes `global_wiki_documents`).

```bash
dotnet ef database update \
  --project src/ContextMemory.Infrastructure \
  --startup-project src/ContextMemory.Api
```

To add a new migration:

```bash
dotnet ef migrations add <MigrationName> \
  --project src/ContextMemory.Infrastructure \
  --startup-project src/ContextMemory.Api
```

---

## Language and localization

| Layer | Language |
|---|---|
| Source code, logs, HTTP errors, Admin UI | **English** |
| README, `.env.example`, config templates | **English** |
| LLM prompts, wiki schema, HITL keywords, tool outputs to the model | **Tenant locale** (`DefaultLanguage`, `WikiSchema`, `BasePersona`) |

The seed app in `appsettings.json` uses `en-US`. Tenants can set `DefaultLanguage` to `pt-PT` (or any BCP-47 tag) and customize `WikiSchema` / personas for localized assistant behavior. Session wiki defaults are in `SessionDefaults.cs`; override per tenant via runtime config.

---

## Security and compliance

- Full tool isolation per tenant
- Restricted egress by default; explicit exceptions per tool/host
- Mandatory HITL for configurable destructive actions
- Append-only wiki log per session (agentic checkpoints)
- Rate limits with extra cost accounting for agentic loops
- **Do not commit real credentials** — `appsettings.Development.json`, `.env`, and `data/` are gitignored; use User Secrets, environment variables, or Key Vault in production
- Rotate any credentials that were ever committed to version control

---

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| `docker pull` / health OK but chat hangs or 502/503 | Ollama not reachable from the container | On the **host**: `ollama serve` + `ollama pull qwen3.5:9b`. Keep `ContextMemory__OllamaEndpoint=http://host.docker.internal:11434` (Compose/scripts already set `host-gateway`). On Linux without Docker Desktop, confirm `host.docker.internal` resolves. |
| Health shows Ollama unhealthy | Wrong endpoint or firewall | From the API container or host, `curl http://host.docker.internal:11434/api/tags`. Override with `-e ContextMemory__OllamaEndpoint=...` or `OLLAMA_ENDPOINT` in `.env`. |
| Port already in use (`5100` / `5200`) | Another process bound the port | Change `API_PORT` / `ADMIN_PORT`, or stop the other process / previous container: `docker rm -f contextmemory-api contextmemory-admin`. |
| Admin: “Not connected” / 401 | Missing or wrong Master Key | Settings → Master Key = `ContextMemory:MasterKey` (demo: `cm_master_dev_key_change_me`). For Compose/GHCR Admin, URL from the **Admin container** is `http://api:8080`, not `localhost`. Click **Test connection**. |
| Admin sidebar / menu does nothing | Stale browser cache after upgrade | Hard refresh. Current Admin uses a Blazor toggle (`admin-shell.js`), not AdminLTE jQuery. |
| Chat Lab 401 | Using Master Key as app key | Chat needs the **app** API key (`cm_live_...`) + `X-App-Id`. Master Key is only for Admin APIs. |
| Model not found / empty replies | Model not pulled or name mismatch | `ollama pull <model>` and set the same id in app config / request body (`llmModel` / `"model"`). |
| Custom OpenAI-compatible URL fails | Missing `/v1` or wrong backend | Set backend to `openai-compatible` (or `openai`) and `llmEndpoint` to the server base URL; the gateway appends `/v1` if needed. |
| Data lost after `docker rm` without volume | Ephemeral container FS | Always mount a volume (`-v contextmemory-data:/app/data` or Compose `cm_data`). |

Still stuck? Open a [GitHub issue](https://github.com/Kortexio/ContextMemory/issues) with health JSON (`GET /health`) and sanitized logs.

---

## Tests

```bash
dotnet test tests/ContextMemory.Api.Tests/ContextMemory.Api.Tests.csproj
```

Coverage includes: API contract, session wiki, Global Wiki, web search, agentic E2E (shell/MCP), HITL, streaming, guardrails, validation, prompt profiles.

---

## Repository structure

```
src/
  ContextMemory.Api/              # HTTP gateway, endpoints, middleware, hosting
  ContextMemory.Core/             # Domain, orchestration, contracts, session wiki logic
  ContextMemory.Infrastructure/   # Persistence, HttpClients, tool executors, telemetry
  ContextMemory.Adapters/         # Ollama, OpenAI, LM Studio, web search
  ContextMemory.Admin.UI/         # Blazor component library (Chat Lab / config editors)
  ContextMemory.Admin.Web/        # Runnable Admin console host (http://localhost:5200)
tests/
  ContextMemory.Api.Tests/        # Integration and E2E tests
```

**Dependency graph:** `Api → Infrastructure → Core` · `Adapters → Core`

Public contracts live in `src/ContextMemory.Core/Contracts/` with XML documentation. Enable Swagger in Development at `/swagger` for the HTTP surface.

---

## Licensing

ContextMemory is released under the **GNU Affero General Public License v3.0 (AGPL-3.0)**. See [`LICENSE`](LICENSE) for the full text.

In short: you are free to use, modify, and self-host this software, including commercially. If you run a modified version as a network service, the AGPL requires you to make your modified source available to that service's users under the same license.

Want to build a closed-source product on top, or avoid the AGPL's network-copyleft obligations entirely? **[Kortexio Cloud](https://kortexio.io)** gives you the hosted gateway under commercial terms — no copyleft reach into your application code. For a self-hosted commercial license or enterprise deployment (ACA pools, internal MCP, admin SSO), contact the commercial team via [kortexio.io](https://kortexio.io).

---

## Support and contribution

Contributions are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md) for the language policy, local setup, and PR guidelines. Open a [GitHub issue](https://github.com/Kortexio/ContextMemory/issues) to report a bug or propose an improvement.

For enterprise integration (ACA pools, internal MCP, admin SSO), reach the commercial team via [kortexio.io](https://kortexio.io).

---

*ContextMemory — one URL for memory and action. The open-source core of [Kortexio](https://kortexio.io).*
