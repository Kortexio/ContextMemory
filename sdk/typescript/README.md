# @kortexio/contextmemory

## What is ContextMemory?

[ContextMemory](https://github.com/Kortexio/ContextMemory) is an **agentic memory gateway**. Your coding agent or chat app talks to a normal OpenAI-compatible URL (`POST /v1/chat/completions`), and the gateway keeps **markdown memory you can open like a wiki** — plus tools, sandbox, MCP, and human-in-the-loop when you need action.

It is **not** classic RAG (no “inject N chunks into the prompt”). Memory is retrieved on demand inside the agent loop (`wiki_search` and related tools). It is also **not** a vector black box: facts live as files you can read, edit, and version.

Use it when:

- Cursor / Claude / your agent **forgets** staging names, decisions, and project context between sessions  
- You want **auditable** memory (markdown) instead of opaque embeddings  
- You want **one `/v1` URL** for chat + memory + tools, self-hosted or on [Kortexio Cloud](https://kortexio.io)

## What is this package?

A **thin header helper** for TypeScript/JavaScript — **not** a full SDK.

ContextMemory needs a few HTTP headers on every call (`Authorization`, and usually `X-App-Id` / `X-User-Id` / `X-Session-Id`). This package builds those headers (and OpenAI client options) so you do not hand-roll them.

> Beta (`0.0.1-beta.x`). APIs may still change.

## Install

```bash
npm i @kortexio/contextmemory openai
```

## Quick start (OpenAI JS client)

Point the client at your gateway `/v1` base URL (self-host or Cloud):

```ts
import OpenAI from "openai";
import { openAIClientOptions } from "@kortexio/contextmemory";

const client = new OpenAI(
  openAIClientOptions({
    baseUrl: process.env.CONTEXTMEMORY_BASE_URL ?? "http://localhost:5100/v1",
    apiKey: process.env.CONTEXTMEMORY_API_KEY!, // cm_live_… or self-host key
    appId: "demo-dev",
    userId: "user-42",
    sessionId: "sess-abc", // same session → same wiki memory
  })
);

const completion = await client.chat.completions.create({
  model: "qwen3.5:9b",
  messages: [{ role: "user", content: "Remember that my staging DB is postgres-staging." }],
});

console.log(completion.choices[0]?.message?.content);
```

Reuse the same `sessionId` across turns so the gateway attaches the right session wiki.

## Headers only (`fetch`, custom clients)

```ts
import { headers } from "@kortexio/contextmemory";

const res = await fetch("http://localhost:5100/v1/chat/completions", {
  method: "POST",
  headers: headers({
    apiKey: "cm_live_...",
    appId: "demo-dev",
    userId: "user-42",
    sessionId: "sess-abc",
  }),
  body: JSON.stringify({
    model: "qwen3.5:9b",
    messages: [{ role: "user", content: "Hello" }],
  }),
});
```

## API

| Export | Purpose |
| --- | --- |
| `openAIClientOptions(input)` | `{ baseURL, apiKey, defaultHeaders }` for `openai` |
| `headers(input)` | Plain header map for any HTTP client |

**Required:** `apiKey`  
**Optional:** `appId`, `userId`, `sessionId`, `extra` (merged into headers)  
**For OpenAI helper:** also `baseUrl` (trailing slash is stripped)

## Cloud vs self-host

| | Base URL | API key |
| --- | --- | --- |
| **Self-host** | `http://localhost:5100/v1` (or your host) | App key from Admin / config |
| **[Kortexio Cloud](https://kortexio.io)** | Cloud API `/v1` | `cmk_live_…` |

More: [GitHub](https://github.com/Kortexio/ContextMemory) · [why we are not RAG](https://github.com/Kortexio/ContextMemory/blob/main/docs/compare.md) · [MCP aha demo](https://github.com/Kortexio/ContextMemory/blob/main/docs/aha-demo.html)

## License

AGPL-3.0-or-later — same as the ContextMemory core.
