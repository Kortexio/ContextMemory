<p align="center">
  <a href="https://github.com/Kortexio/ContextMemory">
    <img src="docs/images/banner-sm.svg" width="800" alt="Kortexio ContextMemory — Memory you can open as a wiki">
  </a>
</p>

<p align="center">
  <a href="#try-it-in-three-commands"><strong>Try it (3 commands)</strong></a>
  ·
  <a href="docs/self-host.md#engines">Engines</a>
  ·
  <a href="docs/README.md">Docs</a>
  ·
  <a href="docs/compare.md">vs Mem0 / Zep / Letta</a>
</p>

<p align="center">
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-AGPL%203.0-blue.svg" alt="License: AGPL-3.0"></a>
  <a href="https://dotnet.microsoft.com/"><img src="https://img.shields.io/badge/.NET-9.0-512BD4" alt=".NET 9"></a>
  <a href="https://github.com/users/Kortexio/packages/container/package/contextmemory"><img src="https://img.shields.io/badge/ghcr.io-contextmemory-blue?logo=docker" alt="Docker GHCR"></a>
  <a href="https://github.com/Kortexio/ContextMemory/actions/workflows/docker-publish.yml"><img src="https://img.shields.io/github/actions/workflow/status/Kortexio/ContextMemory/docker-publish.yml?branch=main&label=docker" alt="Docker CI"></a>
  <a href="https://github.com/Kortexio/ContextMemory/actions/workflows/dotnet-tests.yml"><img src="https://img.shields.io/github/actions/workflow/status/Kortexio/ContextMemory/dotnet-tests.yml?branch=main&label=tests" alt="Tests"></a>
  <a href="https://github.com/Kortexio/ContextMemory/actions/workflows/e2e-llamacpp.yml"><img src="https://img.shields.io/github/actions/workflow/status/Kortexio/ContextMemory/e2e-llamacpp.yml?branch=main&label=e2e%3A%20llama.cpp" alt="e2e: llama.cpp"></a>
</p>

<p align="center">
  <strong>Self-hosted memory gateway for your llama.cpp / vLLM server.</strong>
</p>

<p align="center">
  Put one OpenAI-compatible <code>/v1</code> URL in front of your engine. Your client sends only the new message;
  the gateway keeps session memory as <strong>markdown you can open, edit, and diff</strong>. No vector DB, no client rewrite.
</p>

---

## Try it in three commands

```bash
git clone https://github.com/Kortexio/ContextMemory.git && cd ContextMemory
docker compose -f docker-compose.yml -f docker-compose.llamacpp.yml up --build -d   # CPU; GPU: docker-compose.vllm.yml
./scripts/aha-chat.sh                                                             # Windows: .\scripts\aha-chat.ps1
```

`aha-chat` sends two requests in the same session. The second one carries **only** the new question:

```text
==> Turn 1: "Remember this for later: our staging database host is postgres-staging-01."
==> Turn 2: "What is our staging database host?"   (no history in the request body)
AHA OK — the client sent no history; the gateway remembered 'postgres-staging-01'.
```

The same check runs in CI against a real `llama-server` on every push ([`e2e-llamacpp`](.github/workflows/e2e-llamacpp.yml)). First start downloads a ~2 GB GGUF; pick another model with `LLAMACPP_HF_MODEL` ([Engines](docs/self-host.md#engines)). Admin UI: `http://localhost:5200`.

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

**LLM engines:** ContextMemory does **not** ship or lock to one inference stack. Per tenant you pick any OpenAI-compatible `/v1` host — Ollama, vLLM, LM Studio, [ExLlamaSharp](https://github.com/Kortexio/ExLlamaSharp), OpenAI, Azure-compatible, LiteLLM, custom. Compose ships llama.cpp and vLLM overrides; swap engines per app in **Admin → Config → LLM**.

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

## More ways to run

The gateway talks OpenAI-compatible `/v1` to the engine (and Ollama native `/api/chat` when you need `num_ctx`). Change the engine anytime in **Admin → Config → LLM** or `PATCH /admin/apps/{id}/config`.

### Engine already running (llama-server, vLLM, LM Studio, …) — API image only

```bash
docker run --rm -p 5100:8080 \
  -v contextmemory-data:/app/data \
  -e ContextMemory__MasterKey=cm_master_dev_key_change_me \
  -e ContextMemory__Apps__demo-dev__ApiKey=cm_live_dev_key_change_me \
  -e ContextMemory__Apps__demo-dev__LlmBackend=openai-compatible \
  -e ContextMemory__Apps__demo-dev__LlmModel=local-model \
  -e ContextMemory__Apps__demo-dev__LlmEndpoint=http://host.docker.internal:8080 \
  --add-host=host.docker.internal:host-gateway \
  ghcr.io/kortexio/contextmemory:latest
```

Host-level default for all apps: `ContextMemory__LlmEndpoint`. Ollama on the host works too (`ContextMemory__LlmEndpoint=http://host.docker.internal:11434`). Engine flags that matter (llama.cpp `--jinja`, vLLM tool parser): [Engines](docs/self-host.md#engines). Full stack (API + Admin + MCP + sandbox): [`docs/self-host.md`](docs/self-host.md).

### Cursor / Claude memory (MCP)

```bash
git clone https://github.com/Kortexio/ContextMemory.git
cd ContextMemory/mcp-server && npm install && node print-mcp-config.mjs
```

Paste into **Cursor → Settings → MCP** (or `~/.cursor/mcp.json`). Same snippet works for Claude Desktop. Details: [`mcp-server/README.md`](mcp-server/README.md).

Then, in two separate chats:

| Chat | You say | Agent should |
|---|---|---|
| **A** | `Remember: staging DB is postgres-staging-01` | `memory_save` |
| **B** (new) | `What is our staging DB?` | `memory_search` + answer |

Same flow without Cursor (wiki API, no LLM): `./scripts/aha-demo.sh` or `.\scripts\aha-demo.ps1`.

### No GPU, no ops: Kortexio Cloud

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
  -d '{"model":"local-model","messages":[{"role":"user","content":"Hello"}]}'
```

Thin header helpers (**not** full SDKs): [`@kortexio/contextmemory`](https://www.npmjs.com/package/@kortexio/contextmemory) · [`kortexio-contextmemory`](https://pypi.org/project/kortexio-contextmemory/)

---

## Documentation & support

| Doc | Topic |
|---|---|
| [docs/compare.md](docs/compare.md) | Why it exists · vs Mem0 / Zep / Letta · **why we are not RAG** |
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

**AGPL-3.0** for this open-source core — self-host it freely, including commercially. Need to embed it in a closed-source product without AGPL obligations? Use [Kortexio Cloud](https://kortexio.io) or a commercial license. See [docs/license-and-support.md](docs/license-and-support.md).
