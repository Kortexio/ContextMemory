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
  <a href="docs/aha-demo.html">Aha storyboard (GIF)</a>
  ·
  <a href="docs/show-hn.md">Show HN</a>
</p>

<p align="center">
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-AGPL%203.0-blue.svg" alt="License: AGPL-3.0"></a>
  <a href="https://dotnet.microsoft.com/"><img src="https://img.shields.io/badge/.NET-9.0-512BD4" alt=".NET 9"></a>
  <a href="https://github.com/users/Kortexio/packages/container/package/contextmemory"><img src="https://img.shields.io/badge/ghcr.io-contextmemory-blue?logo=docker" alt="Docker GHCR"></a>
  <a href="https://github.com/Kortexio/ContextMemory/actions/workflows/docker-publish.yml"><img src="https://img.shields.io/github/actions/workflow/status/Kortexio/ContextMemory/docker-publish.yml?branch=main&label=docker" alt="Docker CI"></a>
  <a href="https://github.com/Kortexio/ContextMemory/actions/workflows/dotnet-tests.yml"><img src="https://img.shields.io/github/actions/workflow/status/Kortexio/ContextMemory/dotnet-tests.yml?branch=main&label=tests" alt="Tests"></a>
  <a href="https://github.com/Kortexio/ContextMemory/commits/main"><img src="https://img.shields.io/github/commit-activity/m/Kortexio/ContextMemory?style=flat-square" alt="GitHub commit activity"></a>
</p>

<p align="center">
  <strong>Your agent forgets. Fix that with memory you can open like a wiki.</strong>
</p>

<p align="center">
  One OpenAI-compatible <code>/v1</code> URL: wiki memory, agentic tool loop, skills/guardrails, MCP, sandbox, and HITL —
  self-hosted or <a href="https://kortexio.io">Cloud</a>. <strong>Bring your own LLM</strong> (any OpenAI-compatible engine).
  Not a vector black box. Not classic RAG inject.
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
│  LLM backend (per app — BYO engine)        │
└───────┬──────────────────┬─────────────────┘
        ▼                  ▼
 sandbox-runtime      mcp-runtime / MCP servers
 (shell/python/node)  (HTTP + stdio, OAuth)
 or Azure ACA sessions
```

**Honest boundaries:** this is a **gateway + server-side harness**, not a client agent framework (LangGraph/CrewAI) and not an agent OS (Letta). You keep your OpenAI client; the loop runs on the server.

**LLM engines:** ContextMemory does **not** ship or lock to one inference stack. Per tenant you pick any OpenAI-compatible `/v1` host — Ollama, vLLM, LM Studio, [ExLlamaSharp](https://github.com/Kortexio/ExLlamaSharp), OpenAI, Azure-compatible, LiteLLM, custom. Compose defaults to Ollama only for zero-friction local DX; swap in **Admin → Config → LLM**.

How we compare (Mem0 / Zep / Letta / **why we are not RAG**): [`docs/compare.md`](docs/compare.md).

---

## What it gives developers

| You need… | ContextMemory provides… |
|---|---|
| Memory that survives turns without rewriting your client | Session markdown wiki + history inject; send only the new message |
| Memory you can open, edit, audit | Files on disk / Postgres — not opaque embeddings |
| Shared company/docs knowledge in chat | **Global Wiki** digests + on-demand `wiki_search` / `wiki_grep` (**not** classic RAG / embeddings) |
| Tools without a second orchestrator | Same `/v1`: sandbox + MCP + wiki tools |
| Safer agents | Skills & guardrail packs, validators, HITL `[CONFIRM:id]` |
| Cursor / Claude permanent memory fast | MCP wedge: `memory_save` / `memory_search` / `memory_get` |
| Any LLM per tenant | BYO `/v1` — Ollama, vLLM, LM Studio, ExLlamaSharp, OpenAI, Azure-compatible, custom |
| Operate without a test client | **Admin** + **Playground** |
| Full control / zero ops | Docker self-host · [Kortexio Cloud](https://kortexio.io) (`cmk_live_…`) |

---

## Capabilities (summary)

Full detail: [`docs/architecture-and-features.md`](docs/architecture-and-features.md) · Admin: [`docs/admin-ui.md`](docs/admin-ui.md) · HITL: [`docs/hitl.md`](docs/hitl.md).

| Area | Highlights |
|---|---|
| **Memory** | Session wiki + rolling summary; history budgets; Global Wiki digests/FTS/revisions (`asOf`); **no vector RAG** |
| **Agentic** | Server-side tool loop; sandbox; MCP catalog; artifacts; subagents; validators; HITL; egress policy |
| **Skills** | Platform + per-app skills/guardrails (`skill` / `always_on` / `requestable`) |
| **MCP** | Outbound wedge (Cursor → CM) · inbound catalog (CM → your MCP servers) |
| **Ops** | Admin UI · File or Postgres · Prometheus `/metrics` · Compose (API + Admin + mcp-runtime + sandbox) |

<p align="center">
  <img src="docs/images/admin-dashboard.png" width="800" alt="ContextMemory Admin dashboard">
</p>

<p align="center">
  <img src="docs/images/admin-llm-backend.png" width="390" alt="LLM backend picker">
  &nbsp;
  <img src="docs/images/admin-playground.png" width="390" alt="Admin Playground">
</p>

---

## Quickstart (5 minutes)

**Bring your own LLM.** The gateway talks OpenAI-compatible `/v1` (and Ollama native `/api/chat` when you need `num_ctx`). Compose/GHCR defaults point at Ollama on the host for DX — change anytime in **Admin → Config → LLM** or `PATCH /admin/apps/{id}/config`.

### 1a. Start with any `/v1` engine (vLLM, LM Studio, ExLlamaSharp, OpenAI, …)

```bash
docker run --rm -p 5100:8080 \
  -v contextmemory-data:/app/data \
  -e ContextMemory__MasterKey=cm_master_dev_key_change_me \
  -e ContextMemory__Apps__demo-dev__ApiKey=cm_live_dev_key_change_me \
  -e ContextMemory__Apps__demo-dev__LlmBackend=openai-compatible \
  -e ContextMemory__Apps__demo-dev__LlmModel=my-model \
  -e ContextMemory__Apps__demo-dev__LlmEndpoint=http://host.docker.internal:8000 \
  -e ContextMemory__OpenAiEndpoint=http://host.docker.internal:8000 \
  --add-host=host.docker.internal:host-gateway \
  ghcr.io/kortexio/contextmemory:latest
```

Prefer a host-level default for all apps: set `ContextMemory__LlmEndpoint` (alias; falls back to `OllamaEndpoint` for older Compose files).

### 1b. Or start with Ollama on the host (zero-friction DX)

```bash
docker run --rm -p 5100:8080 \
  -v contextmemory-data:/app/data \
  -e ContextMemory__MasterKey=cm_master_dev_key_change_me \
  -e ContextMemory__Apps__demo-dev__ApiKey=cm_live_dev_key_change_me \
  -e ContextMemory__Apps__demo-dev__LlmModel=qwen3.5:9b \
  -e ContextMemory__LlmEndpoint=http://host.docker.internal:11434 \
  --add-host=host.docker.internal:host-gateway \
  ghcr.io/kortexio/contextmemory:latest
```

Full stack (API + Admin + MCP + sandbox): [`docs/self-host.md`](docs/self-host.md) / `docker-compose.yml`. Admin: `http://localhost:5200`.

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

CLI: `./scripts/aha-demo.sh` or `.\scripts\aha-demo.ps1` · storyboard (for GIF recording): [`docs/aha-demo.html`](docs/aha-demo.html)

### Cloud vs self-host

| | **[Kortexio Cloud](https://kortexio.io)** | **Self-host (this repo)** |
|---|---|---|
| Best for | Zero ops | Full control (API + Admin + MCP + sandbox) |
| Key | `cmk_live_…` (no `X-App-Id`) | `cm_live_…` + `X-App-Id` |
| Chat body | Identical OpenAI `/v1` | Identical OpenAI `/v1` |
| LLM | BYO provider in dashboard | BYO engine in Admin / env |

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
| [docs/show-hn.md](docs/show-hn.md) | Suggested Show HN title + blurb |
| [docs/architecture-and-features.md](docs/architecture-and-features.md) | Wiki, temporal facts, agentic, skills, **LLM backends** |
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
