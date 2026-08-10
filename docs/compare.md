> Part of the ContextMemory docs. [Back to README](../README.md).

# Why ContextMemory · how we compare

## Why it exists

| Problem | ContextMemory |
|---|---|
| LLM forgets between messages | Session markdown wiki + history inject |
| Memory is an opaque blob | **Wiki you can read, edit, version** (`asOf` / supersede) |
| Docs live outside the chat | Global Wiki via `wiki_search` in the agentic loop |
| Need tools without a second product | Sandbox / MCP / HITL on the **same** `/v1/chat/completions` |

## How we compare

| | **ContextMemory** | **Mem0** | **Zep** | **Letta** |
|---|---|---|---|---|
| What it is | Gateway + **markdown wiki** | Memory library | Temporal graph (cloud-first) | Stateful agent runtime |
| You can open memory in an editor | **Yes** | No (API objects) | Graph/UI | Memory blocks / ADE |
| OpenAI chat drop-in | **Yes** (you are the endpoint) | No | No | Own agent API |
| Self-host complete | **AGPL**, no gated core | Partial / paid graph | Graphiti + your DB | Different product shape |

**vs Mem0:** wiki + gateway that acts, not only a memory SDK.  
**vs Zep:** bi-temporal markdown revisions, not Neo4j.  
**vs Letta:** any OpenAI/MCP client; you do not adopt their agent OS.  
**LiteLLM** is a router (complementary). **LangChain** is a client — point `base_url` here.

## Why we are not RAG

We are an **alternative** when people search for RAG — not a RAG product. The harness is **Cursor-style dynamic context discovery** (digests + tools + artifacts), backed by a DB/wiki — **without embeddings**.

| Classic RAG inject | ContextMemory |
|---|---|
| Chunks stuffed into the prompt every turn | Session wiki is budgeted memory; Global Wiki uses **top-K digests** + on-demand `wiki_search` / `wiki_grep` |
| Usually embeddings / vector DB | Lexical / Postgres FTS (+ optional regex) on **markdown you can open** — deliberate, not a vector blob |
| Retrieval product bolted onto chat | **One agentic pipeline**: wiki ± sandbox ± MCP ± HITL on the same `/v1/chat/completions` |
| Client often owns orchestration | Client sends a normal chat body; the gateway runs the loop (compaction, lazy tools, subagents) |

From the architecture docs: discovery tools in the loop, **not** a parallel RAG path — and classic RAG inject is explicitly **N/A**. Details: [architecture-and-features.md](architecture-and-features.md#retrieval--agentic-loop).

## Cloud vs self-host

| | **[Kortexio Cloud](https://kortexio.io)** | **Self-host (this repo)** |
|---|---|---|
| Ops | None | You run the .NET 9 gateway |
| Key | `cmk_live_…` (no `X-App-Id`) | `cm_live_…` + `X-App-Id` |
| Chat body | Identical OpenAI `/v1` | Identical OpenAI `/v1` |

Full guides: [Cloud](cloud.md) · [Self-host / Docker](self-host.md)

## Architecture (30 seconds)

```text
Client (Cursor MCP · OpenAI SDK · LangChain)
        │  memory_* tools  or  POST /v1/chat/completions
        ▼
ContextMemory — session wiki · Global Wiki · agentic loop · HITL
        │
        ▼
Ollama / vLLM / OpenAI / …     (+ optional sandbox / MCP integrations)
```

More detail: [architecture-and-features.md](architecture-and-features.md)
