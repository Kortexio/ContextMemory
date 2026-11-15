<p align="center">
  <a href="https://github.com/Kortexio/ContextMemory">
    <img src="docs/images/banner-sm.svg" width="800" alt="Kortexio ContextMemory — Memory you can open as a wiki">
  </a>
</p>

<p align="center">
  <a href="#try-it-in-three-commands"><strong>Try it (3 commands)</strong></a>
  ·
  <a href="#proof-same-model-harness-off--on">Proof</a>
  ·
  <a href="docs/compare.md">vs Mem0 / Zep / Letta</a>
  ·
  <a href="https://kortexio.io">Cloud</a>
</p>

<p align="center">
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-AGPL%203.0-blue.svg" alt="License: AGPL-3.0"></a>
  <a href="https://dotnet.microsoft.com/"><img src="https://img.shields.io/badge/.NET-9.0-512BD4" alt=".NET 9"></a>
  <a href="https://github.com/users/Kortexio/packages/container/package/contextmemory"><img src="https://img.shields.io/badge/ghcr.io-contextmemory-blue?logo=docker" alt="Docker GHCR"></a>
  <a href="https://github.com/Kortexio/ContextMemory/actions/workflows/e2e-llamacpp.yml"><img src="https://img.shields.io/github/actions/workflow/status/Kortexio/ContextMemory/e2e-llamacpp.yml?branch=main&label=e2e%3A%20local%20engine" alt="e2e: local engine"></a>
</p>

---

# Weak model. Strong agent loop.

A model only writes the next tokens. An **agent** is the loop around it: tools, memory, repair, evidence, finish.  
ContextMemory is that loop on the server — so a **weaker model** can still complete agent work on the **same prompts**.

Point any OpenAI-compatible client here (official OpenAI SDKs, LangChain, curl, Cursor). You keep your app. No LangGraph/CrewAI/Letta rewrite.  
One `/v1/chat/completions` URL — markdown memory you can open, plus a harness that recovers tool calls, refuses fake “done”, and terminates.

Optional npm/PyPI packages only build the gateway headers (`Authorization`, `X-App-Id`, …). They are **header helpers**, not a ContextMemory client SDK — the contract is `/v1`.

---

## Proof (same model, harness off → on)

Same model, same prompts, adversarial suite (wiki · MCP · finish · anti-stuck · distractor).  
**Off** = wire without recovery. **On** = weak harness.

| | Off | On | Δ |
|---|---:|---:|---:|
| **Mean score** | **13.5** | **100** | **+86.5** |
| falseDone | 5/5 | 0/5 | — |
| Tasks passed | 0/5 | 5/5 | — |

Model: `qwen3.6-35b-iq3s-mtp` · run `qwen35b-suite-2026-11-15T20-47-56-653Z`

| Task | Off | On | What On fixed |
|---|---:|---:|---|
| Wiki live-data | 10 | 100 | Called tools → real host |
| MCP tool args | 10 | 100 | Valid MCP call → live fact |
| Finish evidence | 25 | 100 | Evidence before Yes/No |
| Anti-stuck | 17.5 | 100 | Pivot after noisy wiki |
| Distractor | 5 | 100 | Authoritative answer, not bait |

Honesty rule: if Off already passes, the task is invalid — we do not publish vanity Δ.  
Full lift list + how to reproduce: [`benchmarks/`](benchmarks/) · small-model guide: [`docs/small-model-guide.md`](docs/small-model-guide.md).

---

## What you actually get

| Outcome | Why it matters |
|---|---|
| **Send only the new message** | Gateway keeps session memory; client stays dumb and portable |
| **Memory you can open** | Markdown wiki — edit, diff, audit. No opaque vector blob |
| **Weak models that finish** | Family/prose recovery, arg repair, finish gate, anti-stuck budget |
| **Tools on the same URL** | Wiki, sandbox, MCP — no second orchestrator |
| **Safer by default** | Skills, guardrails, validators, optional HITL before destructive acts |
| **Any engine** | BYO OpenAI-compatible `/v1` (local or cloud), swap per app |

**Not** classic RAG. **Not** an agent OS. You keep your OpenAI client; the loop runs here.  
Compare: [`docs/compare.md`](docs/compare.md).

---

## Try it in three commands

```bash
git clone https://github.com/Kortexio/ContextMemory.git && cd ContextMemory
docker compose -f docker-compose.yml -f docker-compose.llamacpp.yml up --build -d
./scripts/aha-chat.sh   # Windows: .\scripts\aha-chat.ps1
```

```text
Turn 1: "Remember: staging DB is postgres-staging-01."
Turn 2: "What is our staging database host?"   ← no history in the request
AHA OK — the gateway remembered it.
```

Same check runs in CI on every push. Admin UI: `http://localhost:5200`.  
Engines / Compose: [`docs/self-host.md`](docs/self-host.md).

### Cursor / Claude (30 seconds)

```bash
cd mcp-server && npm install && node print-mcp-config.mjs
```

Paste into Cursor MCP settings. Chat A: `Remember: staging DB is postgres-staging-01` → Chat B (new): `What is our staging DB?`

### Already have an LLM?

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

Zero ops: **[Kortexio Cloud](https://kortexio.io)** (`cmk_live_…`). Self-host keeps full control (`cm_live_…` + `X-App-Id`).

Optional header helpers (same idea in JS and Python): [`@kortexio/contextmemory`](https://www.npmjs.com/package/@kortexio/contextmemory) · [`kortexio-contextmemory`](https://pypi.org/project/kortexio-contextmemory/)

---

## What the harness fixes (in one glance)

| Pain without it | With ContextMemory |
|---|---|
| Model writes tools in prose / weird markup | Lifted into real `tool_calls` (many model families) |
| Empty `{}` MCP args | Schema + arg repair before execute |
| “Done” after only `tool_describe` | Evidence finish gate — refuse discovery-only |
| Same wiki/MCP call forever | Duplicate guard, Continue, force-answer breakers |
| Invented MCP tool names | Remap onto the live catalog |
| Need a frontier model “to agent” | Weak harness closes most of the gap — see proof above |

Deep dive: [`benchmarks/README.md`](benchmarks/README.md).

---

<p align="center">
  <img src="docs/images/admin-dashboard.png" width="800" alt="ContextMemory Admin dashboard">
</p>

---

## Docs

| | |
|---|---|
| [docs/compare.md](docs/compare.md) | Why this exists · vs Mem0 / Zep / Letta · not RAG |
| [docs/small-model-guide.md](docs/small-model-guide.md) | Harden local / small LLMs |
| [benchmarks/](benchmarks/) | Adversarial A/B (honesty over vanity) |
| [docs/self-host.md](docs/self-host.md) · [docs/cloud.md](docs/cloud.md) | Docker · Cloud |
| [docs/architecture-and-features.md](docs/architecture-and-features.md) | Internals when you need them |
| [docs/README.md](docs/README.md) | Full index |

Website: [kortexio.io](https://kortexio.io) · [hello@kortexio.io](mailto:hello@kortexio.io)

---

## License

**AGPL-3.0** — self-host freely, including commercially. Embed in closed-source without AGPL obligations? Use [Kortexio Cloud](https://kortexio.io) or a commercial license → [docs/license-and-support.md](docs/license-and-support.md).
