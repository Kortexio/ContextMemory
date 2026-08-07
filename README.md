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
  Not a vector black box. Not classic RAG inject. One OpenAI-compatible URL for markdown memory, tools, and HITL — or wire Cursor/Claude in minutes via MCP.
</p>

---

## Introduction

[ContextMemory](https://github.com/Kortexio/ContextMemory) is the open-source **agentic memory gateway** behind [Kortexio](https://kortexio.io). Drop it in as `POST /v1/chat/completions`: session markdown wiki, Global Wiki (`wiki_search` in the agentic loop — **not** classic RAG inject), sandbox/MCP/HITL when you need action.

### Key features

- **Wiki memory** — read, edit, version (`asOf` / supersede). Lexical/FTS on markdown — not an opaque embedding store
- **Drop-in chat URL** — OpenAI `/v1` with the SDKs you already use
- **Any OpenAI-compatible LLM** — Ollama, vLLM, LM Studio, OpenAI, or your own `/v1` endpoint (including Azure OpenAI–compatible URLs). Per-tenant, at runtime — not locked to Ollama
- **MCP wedge** — `memory_save` / `memory_search` / `memory_get` for Cursor & Claude in minutes
- **Agentic on one URL** — sandbox, MCP integrations, skills/guardrails, HITL
- **Admin console** — configure backends, MCP, sandbox, and prove it in Playground (timeline + HITL)
- **Self-host or Cloud** — AGPL core here; zero-ops on [Kortexio Cloud](https://kortexio.io) (EU, BYOK)

How we compare (Mem0 / Zep / Letta / why we are **not** RAG): [`docs/compare.md`](docs/compare.md). Messaging source: [`blueprint/MESSAGING.md`](blueprint/MESSAGING.md).

---

## Quickstart (5 minutes)

### 1. Start the gateway

Default demo points at Ollama on the host. Swap the backend anytime in **Admin → Config → LLM** (`openai`, `openai-compatible`, `vllm`, `lmstudio`, …) or via `PATCH /admin/apps/{id}/config`.

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

Admin UI (Compose / `ghcr.io/kortexio/contextmemory-admin`): typically `http://localhost:5200` — see [`docs/admin-ui.md`](docs/admin-ui.md).

No Docker? Use **[Kortexio Cloud](https://kortexio.io)** (`cmk_live_…`) and set `CONTEXTMEMORY_BASE_URL` to the cloud API.

### 2. Wire MCP into Cursor

```bash
git clone https://github.com/Kortexio/ContextMemory.git
cd ContextMemory/mcp-server && npm install && node print-mcp-config.mjs
```

Paste the JSON into **Cursor → Settings → MCP** (or `~/.cursor/mcp.json`). Same snippet works for Claude Desktop. Details: [`mcp-server/README.md`](mcp-server/README.md).

### 3. Aha

| Chat | You say | Agent should |
|---|---|---|
| **A** | `Remember: staging DB is postgres-staging-01` | `memory_save` |
| **B** (new) | `What is our staging DB?` | `memory_search` + answer |

CLI proof: `./scripts/aha-demo.sh` or `.\scripts\aha-demo.ps1` · GIF storyboard: [`docs/aha-demo.html`](docs/aha-demo.html)

### What the Admin gives you

| Need | Where |
|---|---|
| Point a tenant at OpenAI / vLLM / LM Studio / custom `/v1` | Config → LLM |
| Sandbox shell/python/node or ACA sessions | Config → Agentic |
| MCP servers (HTTP/stdio, OAuth, allow/deny) | Config → Agentic |
| Skills & guardrail packs | Skills + per-app policies |
| Prove memory + tools + HITL without a client | Playground |

<p align="center">
  <img src="docs/images/admin-dashboard.png" width="800" alt="ContextMemory Admin dashboard">
</p>

<p align="center">
  <img src="docs/images/admin-llm-backend.png" width="390" alt="LLM backend picker">
  &nbsp;
  <img src="docs/images/admin-agentic.png" width="390" alt="Agentic gateway config">
</p>

More: [`docs/admin-ui.md`](docs/admin-ui.md) · Playground / Skills: [`admin-playground.png`](docs/images/admin-playground.png) · [`admin-skills.png`](docs/images/admin-skills.png)

### Cloud vs self-host

| | **[Kortexio Cloud](https://kortexio.io)** | **Self-host (this repo)** |
|---|---|---|
| Best for | Zero ops | Full control on your infra |
| Key | `cmk_live_…` (no `X-App-Id`) | `cm_live_…` + `X-App-Id` |
| Chat body | Identical OpenAI `/v1` | Identical OpenAI `/v1` |

Guides: [Cloud](docs/cloud.md) · [Self-host](docs/self-host.md)

### Chat drop-in (optional)

```bash
curl -X POST http://localhost:5100/v1/chat/completions \
  -H "Content-Type: application/json" \
  -H "X-App-Id: demo-dev" -H "X-User-Id: user-42" -H "X-Session-Id: sess-abc" \
  -H "Authorization: Bearer cm_live_dev_key_change_me" \
  -d '{"model":"qwen3.5:9b","messages":[{"role":"user","content":"Hello"}]}'
```

Thin header helpers (not full SDKs): [sdk/python](sdk/python) · [sdk/typescript](sdk/typescript)

---

## Documentation & support

| Doc | Topic |
|---|---|
| [docs/compare.md](docs/compare.md) | Why it exists · vs Mem0 / Zep / Letta · **why we are not RAG** |
| [docs/cloud.md](docs/cloud.md) | Kortexio Cloud |
| [docs/self-host.md](docs/self-host.md) | Docker / GHCR / Compose |
| [docs/architecture-and-features.md](docs/architecture-and-features.md) | Wiki, temporal facts, agentic, skills |
| [docs/api.md](docs/api.md) | HTTP API |
| [docs/admin-ui.md](docs/admin-ui.md) | Admin UI |
| [docs/hitl.md](docs/hitl.md) · [docs/ops.md](docs/ops.md) | HITL · ops & troubleshooting |
| [docs/README.md](docs/README.md) | Full docs index |

Website: [kortexio.io](https://kortexio.io) · Email: [hello@kortexio.io](mailto:hello@kortexio.io)

---

## License

**AGPL-3.0** for this open-source core. Commercial / hosted offerings: [kortexio.io](https://kortexio.io). See [docs/license-and-support.md](docs/license-and-support.md).
