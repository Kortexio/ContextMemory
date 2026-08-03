# ContextMemory Agentic Gateway

[![License: AGPL-3.0](https://img.shields.io/badge/License-AGPL%203.0-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-9.0-512BD4)](https://dotnet.microsoft.com/)
[![Docker](https://img.shields.io/badge/ghcr.io-contextmemory-blue?logo=docker)](https://github.com/users/Kortexio/packages/container/package/contextmemory)
[![CI](https://github.com/Kortexio/ContextMemory/actions/workflows/docker-publish.yml/badge.svg)](https://github.com/Kortexio/ContextMemory/actions/workflows/docker-publish.yml)

**The only agent memory you can read, edit, and version like a wiki.**

Give Cursor and Claude permanent memory with one MCP config — or point any OpenAI-compatible client at the same gateway for chat + tools (sandbox, MCP, HITL).

> Markdown wiki · AGPL self-host · [Kortexio Cloud](https://kortexio.io)

---

## 5-minute start (Cursor / Claude)

**1. Run the gateway**

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

Prefer hosted? Use **[Kortexio Cloud](https://kortexio.io)** (`cmk_live_…`) instead of Docker.

**2. Wire MCP**

```bash
git clone https://github.com/Kortexio/ContextMemory.git
cd ContextMemory/mcp-server && npm install && node print-mcp-config.mjs
```

Paste into **Cursor → Settings → MCP** (or `~/.cursor/mcp.json`).

**3. Aha**

| Chat | You say | Agent should |
|---|---|---|
| **A** | `Remember: staging DB is postgres-staging-01` | `memory_save` |
| **B** (new) | `What is our staging DB?` | `memory_search` + answer |

CLI: `./scripts/aha-demo.sh` · `.\scripts\aha-demo.ps1` · storyboard: [`docs/aha-demo.html`](docs/aha-demo.html)

---

## Docs

| Want | Go to |
|---|---|
| Cloud vs self-host | [docs/cloud.md](docs/cloud.md) · [docs/self-host.md](docs/self-host.md) |
| Why / vs Mem0 · Zep · Letta | [docs/compare.md](docs/compare.md) |
| Architecture & features | [docs/architecture-and-features.md](docs/architecture-and-features.md) |
| API · Admin · HITL · ops | [docs/README.md](docs/README.md) |
| Python / TypeScript helpers | [sdk/python](sdk/python) · [sdk/typescript](sdk/typescript) |

**License:** AGPL-3.0 · commercial / hosted: [kortexio.io](https://kortexio.io) · [details](docs/license-and-support.md)
