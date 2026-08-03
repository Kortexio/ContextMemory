# ContextMemory Agentic Gateway

[![License: AGPL-3.0](https://img.shields.io/badge/License-AGPL%203.0-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-9.0-512BD4)](https://dotnet.microsoft.com/)
[![Docker](https://img.shields.io/badge/ghcr.io-contextmemory-blue?logo=docker)](https://github.com/users/Kortexio/packages/container/package/contextmemory)
[![CI](https://github.com/Kortexio/ContextMemory/actions/workflows/docker-publish.yml/badge.svg)](https://github.com/Kortexio/ContextMemory/actions/workflows/docker-publish.yml)

**Give Cursor and Claude permanent memory — one MCP config. Same gateway can also be your OpenAI-compatible chat URL when you need retrieval + action.**

> **Wedge (start here):** [Permanent memory for Cursor / Claude in ~5 minutes](#give-cursor--claude-permanent-memory-5-minutes) · [Record the 20s aha GIF](docs/aha-demo.html)

ContextMemory is a context and agent proxy for applications that talk to LLMs. The **preferred public wire format is OpenAI-compatible**: `POST /v1/chat/completions` (response with `choices[]`) and `GET /v1/models`. The same OpenAI-compatible protocol is used **downstream** to talk to Ollama (`/v1`), vLLM, LM Studio, OpenAI, or any compatible server.

Legacy Ollama-native routes `POST /api/chat` and `POST /api/generate` remain available (deprecated) for older clients.

Behind the scenes, the gateway enriches each turn with session memory (a per-session markdown wiki), optional web search, and — when Global Wiki and/or agentic tools are enabled — an **agentic loop** where **retrieval is a tool** (`wiki_search`) alongside sandbox/MCP, not a separate RAG inject.

**Contents:** [Cursor memory (5 min)](#give-cursor--claude-permanent-memory-5-minutes) · [Why](#why-it-exists) · [How we compare](#how-we-compare) · [Cloud vs self-host](#two-ways-to-run-it) · [Cloud quick start](#quick-start--kortexio-cloud) · [Self-host / Docker](#quick-start--self-host) · [Admin UI guide](#admin-ui-guide) · [Architecture](#architecture-in-30-seconds) · [Features](#features) · [Retrieval + agentic](#retrieval--agentic-loop) · [API](#api--stable-contract) · [Docs backlog](#documentation-backlog) · [Troubleshooting](#troubleshooting) · [License](#licensing)

---

## Give Cursor / Claude permanent memory (5 minutes)

This is the sharpest path: not “memory infrastructure,” but **your coding agent remembers across chats**.

### 1. Start the gateway (~30s)

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

Prefer zero Docker? Use **[Kortexio Cloud](https://kortexio.io)** (`cmk_live_…`) and point `CONTEXTMEMORY_BASE_URL` at the cloud API.

### 2. Wire MCP into Cursor (~1 min)

```bash
git clone https://github.com/Kortexio/ContextMemory.git
cd ContextMemory/mcp-server
npm install
node print-mcp-config.mjs
```

Paste the printed JSON into **Cursor → Settings → MCP** (or `~/.cursor/mcp.json`). Same snippet works for Claude Desktop’s `claude_desktop_config.json`.

### 3. Aha moment (~1 min)

| Chat | You say | Agent should |
|---|---|---|
| **A** | `Remember: staging DB is postgres-staging-01` | Call `memory_save` |
| **B** (new chat) | `What is our staging DB?` | Call `memory_search` and answer |

No SDK. No embeddings setup. Facts land in the Global Wiki (markdown + temporal supersede under the hood).

**CLI proof (no Cursor):** with the gateway up, run `./scripts/aha-demo.sh` or `.\scripts\aha-demo.ps1`.

**GIF asset:** open [`docs/aha-demo.html`](docs/aha-demo.html) fullscreen and screen-record ~20s (auto-loops). That clip is the shareable demo — more important than any paragraph below.

Details: [`mcp-server/README.md`](mcp-server/README.md).

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
| Product/ops docs live outside the chat session | **Global Wiki** — app-scoped KB retrieved on demand via `wiki_search` **inside the agentic loop** (not a separate RAG inject) |
| You need actions (shell, APIs, MCP) without a new endpoint | Agentic loop on the same `/v1/chat/completions`, invisible to the client |
| Each client/tenant needs different tools and rules | Per-app configuration: ACA, self-hosted sandbox, MCP, guardrails, prompts |
| Destructive actions need human control | Blocking human-in-the-loop with wiki checkpoints |
| Streaming with multi-step loops is complex | Internal buffer: client only receives final text; optional progress via metadata |

---

## How we compare

ContextMemory is an **agentic context gateway**, not a memory library or an LLM router. Adjacent tools solve different layers of the stack:

| | **ContextMemory** | **Mem0** | **Zep** | **LiteLLM** |
|---|---|---|---|---|
| Category | Gateway: memory + retrieval + action | Memory library / layer | Temporal memory / context graphs (cloud-first) | LLM proxy / router |
| OpenAI chat wire | Yes — you **are** the `/v1/chat/completions` endpoint | No — you wire an SDK into the app | No — separate memory API/SDK | Yes — routing only, no session wiki |
| Full self-host | Yes (AGPL, no gated core features) | OSS core; advanced graph memory on paid tiers | Full product is cloud; OSS path is Graphiti + your own graph DB | Yes (different problem) |
| Session memory | Per-session markdown wiki injected automatically | Extracted memories via add/search | Temporal facts / context graphs | No |
| Retrieval | `wiki_search` inside the agentic loop | Your RAG / their search APIs | In the Zep layer | No |
| Action (sandbox / MCP / HITL) | Same URL | No | No | Limited pass-through |
| Typical integration | Change `base_url` | SDK + wiring | SDK + wiring | Change `base_url` (no memory) |

**LangChain** (and LlamaIndex, Vercel AI SDK, etc.) are **client frameworks**, not direct competitors. Mem0/Zep plug *into* the framework; ContextMemory sits **underneath** — point the OpenAI-compatible chat client at this gateway:

```python
from langchain_openai import ChatOpenAI

llm = ChatOpenAI(
    base_url="http://localhost:5100/v1",
    api_key="cm_live_...",
    default_headers={
        "X-App-Id": "demo-dev",
        "X-User-Id": "user-42",
        "X-Session-Id": "sess-abc",
    },
)
```

Same pattern works for any OpenAI-compatible client: one URL carries session memory, on-demand Global Wiki retrieval, and optional tools — without a separate memory SDK.

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

Run the open-source gateway yourself — the path for **on-prem or fully local** deployments. Unlike Cloud (where the dashboard wires up your LLM for you), here **you point the gateway at your own LLM backend** — endpoint and model — in config. Same OpenAI-compatible chat body and `choices[]` response as Cloud; you supply the `X-App-Id` and use a `cm_live_` key.

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

Builds and starts the **API** (`:5100`), **Admin** (`:5200`), **mcp-runtime** (stdio MCP host), and **sandbox-runtime** (shell/python/node) locally. Requires [Docker](https://docs.docker.com/get-docker/) and an LLM reachable from the containers (Ollama on the host by default).

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
| mcp-runtime | internal `http://mcp-runtime:8080` (`ContextMemory__McpRuntimeUrl`) |
| sandbox-runtime | internal self-hosted sandbox for `shell_execute` / `python_execute` / `node_execute` |
| Demo app | `X-App-Id: demo-dev` / Bearer `cm_live_dev_key_change_me` |
| Master key | `cm_master_dev_key_change_me` |

For a Postgres-backed network overlay (shared Docker network, extra tenants), see `docker-compose.network.yml`.

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

**Chat smoke test against the containerized API** (preferred OpenAI route):

```bash
curl -X POST http://localhost:5100/v1/chat/completions \
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
curl -X POST http://localhost:5100/v1/chat/completions \
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

**Response (OpenAI schema — identical to Cloud):**

```json
{
  "id": "chatcmpl-...",
  "object": "chat.completion",
  "model": "qwen3.5:9b",
  "choices": [{
    "index": 0,
    "message": { "role": "assistant", "content": "..." },
    "finish_reason": "stop"
  }]
}
```

Legacy `POST /api/chat` still returns the Ollama `message` / `done` shape (deprecated).

**Required headers:** `X-App-Id`, `X-User-Id`, `Authorization: Bearer {API_KEY}`  
**Optional:** `X-Session-Id` (generated by the API if omitted).

### 4. Admin UI and Chat Lab

Prefer Docker Compose above, or run the Admin host locally against a local API:

```bash
cd src/ContextMemory.Admin.Web
dotnet run
```

Open **[http://localhost:5200](http://localhost:5200)** → **Settings** → set API URL `http://localhost:5100` and your Master Key (`ContextMemory:MasterKey`) → **Test connection**.

Full walkthrough of every screen: **[Admin UI guide](#admin-ui-guide)**.

The API also serves a short HTML pointer at [http://localhost:5100/admin](http://localhost:5100/admin). Admin HTTP APIs still require the Master Key bearer token.

---

## Admin UI guide

The Admin console (`ContextMemory.Admin.Web`, default **http://localhost:5200**) is a Blazor Server app that talks to the ContextMemory API over HTTP. UI copy is English; field help sits under each control.

| | |
|---|---|
| **Who uses it** | Operators configuring tenants (LLM, memory, agentic, MCP, keys) |
| **Auth** | **Master Key** (`ContextMemory:MasterKey`) for admin APIs — not the app `cm_live_…` key |
| **Where settings live** | Browser local storage (overrides host defaults from `Admin__*` / Compose env) |
| **Image** | `ghcr.io/kortexio/contextmemory-admin` |

### Navigation map

| Menu | Route | Purpose |
|---|---|---|
| Applications | `/` | List tenants, open details / config |
| New application | `/apps/new` | Register a tenant and mint an API key |
| Chat Lab | `/chat` | Interactive chat against an app (memory + agentic) |
| Skills | `/skills` | Shared skills catalog (+ view guardrail packs) |
| Settings | `/settings` | API base URL, Master Key, health |

Per-app pages (from Applications):

| Page | Route | Purpose |
|---|---|---|
| Details | `/apps/{appId}` | Telemetry, wiki/web-search stats, view/rotate API key |
| Config | `/apps/{appId}/config` | LLM, wiki, Global Wiki, web search, rate limits, **Agentic Gateway** |

### First-time setup (Settings)

1. Open **Settings**.
2. Set **API base URL**:
   - Local `dotnet run`: `http://localhost:5100`
   - Docker Compose / GHCR Admin container: `http://api:8080` (server-side calls stay on the Docker network; the browser still opens Admin on `localhost:5200`)
3. Paste the **Master Key** (demo: `cm_master_dev_key_change_me`).
4. Click **Save**, then **Test connection**.
5. Confirm Health: Ollama, Persistence, Apps loaded, optional Database.

**Save** stores values in this browser. **Reset to defaults** clears browser overrides and restores host defaults (`Admin__DefaultApiBaseUrl`, `Admin__DefaultMasterKey`). A link to public Prometheus metrics (`GET /metrics`) appears when connected.

### Applications dashboard

- Cards per app: request count, active users, last-turn wiki pages included/total, source badge (`seed` vs registered).
- Actions: **Details**, **Config**, **Chat Lab**.
- **New application** / **Refresh**. Seed app `demo-dev` appears when the API starts with default config.

### Register an application

**New application** creates an isolated tenant (own API key, session wiki, optional Global Wiki).

| Field | Notes |
|---|---|
| Application name | Operator label only (not an HTTP header) |
| Domain | Prefix for generated `X-App-Id` (e.g. `helpdesk` → `helpdesk-…`) |
| Default language | BCP-47 for prompts (`en-US`, `pt-PT`, …) |
| LLM backend / model / endpoint / API key | Same semantics as Config (below) |
| Persona | Optional base system prompt |

After create, **copy the API key immediately** — it is shown once on this screen. Then open Config, Details, or Chat Lab.

### App details (stats + API key)

`/apps/{appId}` shows:

- Requests, errors, active users, average latency
- Prompt/completion tokens, wiki truncated total
- Last-turn wiki memory (chars, pages included/on disk, compaction ok/err)
- Web search totals when used
- **API key** — show/copy; **Generate new API key** rotates it

For **seed** apps (`appsettings`), rotation lasts until API restart unless you also update `ContextMemory:Apps:{appId}:ApiKey`.

Use the app key in Chat Lab and client apps — never the Master Key.

### App Config (runtime)

`/apps/{appId}/config` patches runtime config (`PATCH /admin/apps/{id}/config`). Changes apply to **new requests** after **Save changes**.

#### LLM

| Control | Meaning |
|---|---|
| Default language | Locale in prompts |
| LLM model | Model id on the backend |
| Backend | `ollama` (default `/v1`), `vllm`, `lmstudio`, `openai`, `openai-compatible`, `custom`, `ollama-native` |
| Endpoint / API key | Optional per-app overrides; empty = host defaults |
| Max history messages | Recent turns kept in the prompt (wiki is separate) |
| Streaming enabled | Allow streaming on legacy `/api/chat` |

Prefer clients calling **`POST /v1/chat/completions`**.

#### Session wiki

Max wiki context chars, compaction threshold (bytes), compaction min pages.

#### Global Wiki

- Enable Global Wiki → exposes `wiki_search` for documents under `/apps/{id}/wiki/…`
- Max Global Wiki tool chars — budget per `wiki_search` call (`0` = service default)

Ingest/digests are API-side; the Admin UI toggles availability and budget. Retrieval runs inside the agentic loop — see [Retrieval + agentic loop](#retrieval--agentic-loop).

#### Web search

Enable, mode (`heuristic` / `llm` / `always` / `off`), provider (`tavily` / `brave`), max results, max ephemeral context chars, persist-to-wiki, telemetry logging. Provider API keys live on the **API host** (`ContextMemory:WebSearch`), not per app.

#### Rate limits

App RPM, per-user RPM, TPM, agentic request weight, agentic tokens per iteration.

#### Agentic Gateway (on the Config page)

| Area | What you configure |
|---|---|
| Enable agentic loop | Multi-step tools on the same chat request |
| Prompt profile | `auto` / `ollama` / `openai` / `claude` |
| Loop guardrails | Validation mode, network egress, max iterations, loop timeout, min answer length, confirmation keywords, allowed hosts, expected regexes, require exit 0, human review on max iterations |
| **Skills & guardrail packs** | Per-app checkboxes from the shared catalog (see Skills page). Omit selection → catalog defaults |
| Execution tools | `self-hosted-sandbox` → sandbox endpoint (Compose: `http://sandbox-runtime:8080`) or `aca-session` → ACA pool URL; runtimes shell/python/node/(custom); `allowEgress` |
| MCP integrations | `http` or `stdio`; name, URL/command+args, auth mode, credential ref, OAuth fields, allow/deny tool lists, timeout, enabled, allowEgress; max MCP tools per turn |

Example stdio MCP is shown in a collapsible on the Config page. After editing MCP servers, rebuild the tool catalog via API if needed: `POST /apps/{appId}/mcp/catalog/rebuild` (Master Key or app auth as configured).

#### Persona and rules

Markdown fields: base persona, business rules, format rules, wiki schema.

### Skills catalog

**Skills** (`/skills`) manages the **shared** catalog (all apps). Enabling packs for a tenant is done on that app’s **Config → Agentic → Skills & guardrail packs**.

| Action | Notes |
|---|---|
| New skill / Edit | Id (slug, create-only), name, category, description, prompt markdown, sort order, default-enabled |
| System skills | Prompt editable; cannot delete |
| Download | Export `.skill.json` |
| Import | `.json` / `.md` / `.skill.json`; optional replace-if-exists |
| Guardrail packs table | Read-only list (`id`, `kind`, default); toggle per app in Config |

### Chat Lab

Interactive tester for a tenant. Uses the **app API key** + `X-App-Id` (Master Key only for “Load app” metadata via admin APIs).

**Connection panel**

| Field | Role |
|---|---|
| App ID / User ID / Session ID | Headers; empty session → API generates |
| App API key | Bearer for chat |
| Model | Body `model` (match app config) |
| Chat / Generate | Legacy `POST /api/chat` or `/api/generate` |
| Streaming | NDJSON token stream |
| Show raw JSON | Debug panel |
| Save locally / Load app / New session | Persist lab settings, pull app summary, reset session id |

**Ollama options** — optional per-request system prompt, temperature, top_p, top_k, num_ctx, repeat_penalty, num_predict, keep_alive, format.

**Main pane** — conversation; agentic timeline (tool steps); HITL banner with `confirm` / `[CONFIRM:id]` and copy token; Stop / Regenerate / Clear UI.

Typical smoke path: Settings connected → open Chat Lab → `demo-dev` + demo key → send a message → same Session ID on the next turn to verify memory.

### Keys cheat sheet

| Secret | Used for | Where |
|---|---|---|
| Master Key (`cm_master_…`) | List apps, patch config, rotate keys, skills catalog | Settings |
| App API key (`cm_live_…`) | Chat, wiki ingest, app-scoped MCP ops | Details / Chat Lab / clients |
| LLM API key | Downstream OpenAI-compatible backends | App Config (optional) |
| MCP / OAuth secrets | Integration tools | Config credential ref / OAuth fields (prefer credential store over inline tokens) |

### Admin troubleshooting

| Symptom | Fix |
|---|---|
| Not connected / cannot list apps | Settings → correct API URL + Master Key → Test connection |
| 401 on admin calls | Master Key ≠ `ContextMemory:MasterKey` |
| Chat Lab 401 | Using Master Key as app key — use `cm_live_…` + `X-App-Id` |
| Compose Admin cannot reach API | From Admin container use `http://api:8080`, not `localhost:5100` |
| Sidebar dead after upgrade | Hard refresh (Blazor `admin-shell.js`, not legacy AdminLTE jQuery) |
| Seed key rotation “lost” on restart | Persist new key in `appsettings` / env for seed apps |

---

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
- **Skills & guardrail packs (configurable per app)** — shared catalog (File or Postgres), seeded on startup; each tenant picks which packs are active via `agentic.policyPacks`.
  - **Skills** — Markdown policy text injected into the agent system prompt (anti-hallucination, wiki-first, MCP preference, Zuora discover-first, …). Create/edit/import/export in Admin **Skills** or `/admin/agentic/skills`.
  - **Guardrail packs** — deterministic validators by `kind` (`url-fetch`, `sandbox-claim`, `tool-failure-disclosure`, `blocked-patterns`) that can reject a final answer and force another loop iteration.
  - **Per-app selection** — `enabledSkillIds` / `enabledGuardrailIds`. Omit both → defaults (`IsDefaultEnabled` in the catalog). Explicit empty arrays → none. Skill `sandbox-facts-selfhosted` only applies when a self-hosted sandbox tool is configured.
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
- **Admin** — Blazor console at `ContextMemory.Admin.Web` (`:5200`); see [Admin UI guide](#admin-ui-guide)
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
| `PUT /apps/{id}/wiki/documents/{documentId}` | Upsert Global Wiki document (storage-only; default supersede on content change) |
| `POST /apps/{id}/wiki/documents/batch` | Batch upsert Global Wiki documents |
| `POST /apps/{id}/wiki/digests/rebuild` | Rebuild LLM digests + `wiki:catalog` |
| `GET /apps/{id}/wiki/documents/{documentId}` | Get active Global Wiki document |
| `GET /apps/{id}/wiki/documents/{documentId}/revisions` | Revision timeline for a document |
| `GET /apps/{id}/wiki/documents` | List Global Wiki documents (`includeSuperseded` optional) |
| `GET /apps/{id}/wiki/audit` | Export wiki revisions (`from` / `to` optional) |
| `DELETE /apps/{id}/wiki/documents/{documentId}` | Soft-delete active revision (closes validity window) |
| `POST /apps/{id}/wiki/query` | Search Global Wiki (`asOf` for point-in-time) |
| `GET /apps/{id}/sessions/{userId}/{sessionId}/wiki` | Compiled session wiki recall |
| `GET /apps/{id}/mcp/servers` | List MCP servers / catalog status for the app |
| `POST /apps/{id}/mcp/catalog/rebuild` | Refresh MCP tool catalog (HTTP + stdio) |
| `POST /apps/{id}/mcp/test/{name}` | Probe an MCP server |
| `POST /apps/{id}/mcp/credentials/{name}` | Upsert MCP credentials |
| `GET /apps/{id}/config` | Runtime config (auth with app API key) |
| `PATCH /admin/apps/{id}/config` | Update config (Master Key), including `GlobalWikiEnabled` |
| `GET /admin/agentic/catalog` | Skills + guardrail packs |
| `POST/PUT/DELETE /admin/agentic/skills...` | Manage skills (import/export supported) |
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
| Global Wiki documents (+ digests / catalog) | ✅ | ✅ |
| MCP catalog + credentials | ✅ | ✅ |
| Agentic skills / guardrail packs | ✅ | ✅ |
| Pending HITL state | ✅ | ✅ |
| Telemetry / rate limits | in-memory | in-memory |

EF Core migrations live in `ContextMemory.Infrastructure` (includes `global_wiki_documents`, MCP catalog, and agentic policy catalog).

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

Coverage includes: API contract (OpenAI `/v1` + legacy), session wiki, Global Wiki (query packing, digests), web search, agentic E2E (shell/MCP), skills/guardrails, HITL, streaming, validation, prompt profiles.

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
mcp-runtime/                      # Node host for stdio MCP packages (/opt/mcps)
sandbox-runtime/                  # Node host for self-hosted shell/python/node tools
tests/
  ContextMemory.Api.Tests/        # Integration and E2E tests
```

**Dependency graph:** `Api → Infrastructure → Core` · `Adapters → Core`

Public contracts live in `src/ContextMemory.Core/Contracts/` with XML documentation. Enable Swagger in Development at `/swagger` for the HTTP surface.

---

## Documentation backlog

Items still thin or missing from this README (code exists; docs TBD or expand later):

| Area | What's missing |
|---|---|
| **MCP catalog** | End-to-end stdio packaging guide (`mcp-runtime` + `/opt/mcps`) and credential modes matrix beyond Config field help |
| **sandbox-runtime** | Security model, env knobs (`SANDBOX_*`), and ACA vs self-hosted comparison beyond the feature bullets |
| **Global Wiki digests** | Operational guidance for large corpora (batch ingest → `digests/rebuild`, when to `force`, catalog size) |
| **Network Compose** | `docker-compose.network.yml` (Postgres, shared network, multi-tenant seeds such as `laravox` / `kyc`) as a first-class quick-start path |
| **OpenAI metadata** | Where agentic progress / HITL confirmation appear on `/v1` responses vs legacy `/api/chat` `context_memory` |
| **Cloud vs OSS deltas** | Explicit matrix of Cloud-only dashboard features vs this repo |

PRs that close any row above are welcome — keep examples in English and prefer `/v1/chat/completions` in new snippets.

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
