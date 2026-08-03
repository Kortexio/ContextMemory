<p align="center">
  <a href="https://github.com/Kortexio/ContextMemory">
    <img src="docs/images/banner-sm.svg" width="800" alt="Kortexio ContextMemory — Memory you can open as a wiki">
  </a>
</p>

<p align="center">
  <a href="https://kortexio.io">Website</a>
  ·
  <a href="docs/README.md">Docs</a>
  ·
  <a href="docs/aha-demo.html">Demo</a>
  ·
  <a href="https://kortexio.io">Cloud</a>
</p>

<p align="center">
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-AGPL%203.0-blue.svg" alt="License: AGPL-3.0"></a>
  <a href="https://dotnet.microsoft.com/"><img src="https://img.shields.io/badge/.NET-9.0-512BD4" alt=".NET 9"></a>
  <a href="https://github.com/users/Kortexio/packages/container/package/contextmemory"><img src="https://img.shields.io/badge/ghcr.io-contextmemory-blue?logo=docker" alt="Docker GHCR"></a>
  <a href="https://github.com/Kortexio/ContextMemory/actions/workflows/docker-publish.yml"><img src="https://github.com/Kortexio/ContextMemory/actions/workflows/docker-publish.yml/badge.svg" alt="CI"></a>
  <a href="https://github.com/Kortexio/ContextMemory/commits/main"><img src="https://img.shields.io/github/commit-activity/m/Kortexio/ContextMemory?style=flat-square" alt="GitHub commit activity"></a>
</p>

<p align="center">
  <strong>The only agent memory you can read, edit, and version like a wiki.</strong>
</p>

<p align="center">
  Give Cursor and Claude permanent memory with one MCP config — or point any OpenAI-compatible client at the same gateway for chat + tools (sandbox, MCP, HITL).
</p>

---

## Introduction

[ContextMemory](https://github.com/Kortexio/ContextMemory) is the open-source **agentic memory gateway** behind [Kortexio](https://kortexio.io). It sits between your app and your LLM as an OpenAI-compatible endpoint: session markdown wiki, Global Wiki retrieval (`wiki_search`), and optional tools — without rewriting your client.

### Key features

- **Wiki memory** — per-session markdown you can open, edit, and version (`asOf` / supersede), not an opaque vector blob
- **Drop-in chat URL** — `POST /v1/chat/completions` with the SDKs you already use
- **MCP wedge** — `memory_save` / `memory_search` / `memory_get` for Cursor & Claude in minutes
- **Agentic on one URL** — sandbox, MCP integrations, and HITL when you need action, not only recall
- **Self-host or Cloud** — AGPL core here; zero-ops on [Kortexio Cloud](https://kortexio.io) (EU)

How we compare to Mem0, Zep, and Letta: [`docs/compare.md`](docs/compare.md).

---

## Quickstart (5 minutes)

### 1. Start the gateway

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

Thin helpers: [sdk/python](sdk/python) · [sdk/typescript](sdk/typescript)

---

## Documentation & support

| Doc | Topic |
|---|---|
| [docs/compare.md](docs/compare.md) | Why it exists · vs Mem0 / Zep / Letta |
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
