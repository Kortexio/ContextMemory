# ContextMemory — Messaging & dissemination source of truth

Use this file for README copy, LinkedIn/X/dev.to posts, release announcements, and launch scripts.
Language for public posts: **English** (product audience). Internal notes may be PT.

---

## Voice (non-negotiable)

- **Short.** Hook ≤ 12 words. Body 1–2 lines. One CTA.
- **Direct.** Lead with pain or outcome, not feature lists.
- **Provocation allowed** — only factual architecture comparisons (wiki vs blob, drop-in vs new OS, AGPL core vs gated). Never price-bash or unfalsifiable claims.
- **Social ≠ changelog.** Long detail belongs on GitHub Releases / dev.to only.

### Do not say

| Claim | Reality |
|---|---|
| "We are RAG" / "RAG solution" | **We do not do classic RAG.** `wiki_search` is an agentic-loop tool; classic inject = N/A. |
| "Semantic / vector memory" | Lexical/FTS + scoring on markdown — deliberate, not a gap. |
| "Native Anthropic / Azure adapter" | Wire is OpenAI-compatible (`/v1`). Claude = prompt profile. Azure works via compatible URL. |
| "Any database" | File or Postgres only. |
| "Mature Python/TS SDKs" | Thin header helpers (`v0.1.0`), not full clients. |

---

## Pain pitches (pick one)

1. **Your agent forgets everything between chats.** Permanent markdown memory — open it like a wiki.
2. **Agent memory is a black box.** Ours is files you can read, edit, and version (`asOf` / supersede).
3. **RAG injects text. Agents need memory + action.** One OpenAI URL: wiki, sandbox, MCP, HITL.
4. **Cursor/Claude start from zero every session.** One MCP config. Five minutes to the aha.

---

## Proof inventory (anchor claims here)

1. Not classic RAG — wiki tool inside one agentic pipeline.
2. Bi-temporal Global Wiki (`asOf`, supersede, audit API).
3. Dual execution: ACA Dynamic Sessions **or** self-hosted sandbox.
4. Full MCP gateway: HTTP + stdio, per-tenant OAuth, allow/deny, catalog rebuild.
5. Skills & guardrails as config (platform + per-app, import/export).
6. Production HITL (`[CONFIRM:id]`, session log checkpoint).
7. Per-tenant LLM backend at runtime (Ollama / vLLM / LM Studio / OpenAI / openai-compatible).
8. Postgres HA path + File for single-node.
9. Streaming that does not leak tool chatter.
10. Real test suite (~163 cases) — add CI badge when `dotnet-tests` workflow ships.
11. AGPL full core + Cloud BYOK (no token markup).
12. Admin Playground: timeline + HITL without writing client code.
13. Postgres FTS (`tsvector` + GIN) for Global Wiki.
14. MCP wedge tools: `memory_save`, `memory_search`, `memory_get`, `wiki_search`, `session_recall`.
15. MCP tool selection per turn (`MaxMcpToolsPerTurn`).
16. Prometheus `/metrics` + OpenTelemetry (Aspire).

---

## CTA ladder

1. Star → https://github.com/Kortexio/ContextMemory  
2. Aha (MCP) → `mcp-server` + [`docs/aha-demo.html`](../docs/aha-demo.html)  
3. Cloud trial → https://kortexio.io (`cmk_live_…`)  
4. Talk → hello@kortexio.io  

Primary CTA in hero: **Star + try MCP** or **Cloud**. One primary per surface.

---

## Audience angles

| Audience | Lead with | CTA |
|---|---|---|
| Solo Cursor/Claude users | Permanent memory in 5 min | MCP config |
| Eng teams shipping agents | Same `/v1` + sandbox/MCP/HITL | Docker / Cloud |
| Tech leads / CTOs | Auditable wiki memory, AGPL self-host, EU Cloud | compare.md + Cloud |

---

## Provocation bank (factual)

1. Mem0 stores memory. ContextMemory stores memory you can open in an editor.
2. Your agent memory is a black box. Ours is a markdown wiki.
3. Classic RAG injects chunks. We don't — `wiki_search` is a tool in the agentic loop.
4. Zep wants a graph. We version markdown facts with `asOf`.
5. Letta is an agent OS. We are a drop-in OpenAI endpoint — keep your client.
6. Opaque vectors don't have an audit trail. Our Global Wiki does.
7. "Supports MCP" often means a proxy. We do catalog, OAuth per tenant, allow/deny.
8. Recall without action is half a product. Same URL: wiki + sandbox + HITL.
9. Self-host that gates the core isn't self-host. AGPL here is the whole gateway.
10. Locked to one local LLM? Point any OpenAI-compatible backend — per tenant, at runtime.

---

## Weekly hooks (one strength each — rotate)

| Id | Hook | Strength # |
|---|---|---|
| H01 | Your coding agent forgets staging DB names. Fix that in five minutes. | 14 |
| H02 | "What was true last March?" — bi-temporal wiki, not a vector blob. | 2 |
| H03 | Same chat URL for memory and shell/python/node. | 3 |
| H04 | Destructive tool? HITL pauses until you confirm. | 6 |
| H05 | Not RAG. On-demand `wiki_search` inside the agent loop. | 1 |
| H06 | Skills and guardrails as editable packs — not prompt spaghetti. | 5 |
| H07 | Ollama today, vLLM tomorrow — same gateway, per-app config. | 7 |
| H08 | Admin Playground: watch tool steps and HITL without a client. | 12 |

Track used hooks in GitHub Issues with label `content-hook` (or mark in PR description when publishing).

---

## Release post templates

### LinkedIn / Discord short

```
{HOOK ≤ 12 words}

{1–2 lines: what shipped + why it matters}

Try it: {CTA URL}
#dotnet #opensource #AI #LLM #agents #MCP
```

Optional second line from Provocation bank when the release touches that strength.

### X (≤ 280)

```
{HOOK}

{one proof line}
{short URL}
```

### dev.to title style

Short and direct: e.g. `ContextMemory {version}: {one outcome}` — body can be the full release notes.

---

## Launch scripts (manual — do not automate Reddit/HN)

### Show HN

```
Show HN: ContextMemory – agent memory you open like a wiki (not a vector blob)
```

First comment: one pain, MCP aha, AGPL + Cloud link. Answer comments in the first 2 hours.

### Reddit (adapt per sub)

- r/LocalLLaMA: self-host + OpenAI-compatible + Ollama/vLLM  
- r/selfhosted: Docker/Compose + AGPL  
- r/dotnet: .NET 9 gateway  

No spam cross-post same day. Lead with problem, not feature dump.

### Product Hunt

Tagline: `Agent memory you can read like a wiki`  
First comment: MCP wedge + OpenAI drop-in + not classic RAG.

### Awesome-list / directories (checklist)

- [ ] awesome-mcp-servers  
- [ ] awesome-llm-apps / awesome-ai-agents  
- [ ] awesome-selfhosted  
- [ ] AlternativeTo, LibHunt  
- [ ] Product Hunt launch day  

---

## GitHub topics (use these — not `rag`)

`agentic`, `ai-agent`, `ai-memory`, `agent-memory`, `llm-memory`, `mcp`, `mcp-server`, `model-context-protocol`, `openai-compatible`, `dotnet`, `self-hosted-ai`, `cursor-mcp`, `claude-mcp`, `rag-alternative`, `agentic-ai`

Never: `rag`, `retrieval-augmented-generation` as topic labels.
