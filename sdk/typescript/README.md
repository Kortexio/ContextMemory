# @kortexio/contextmemory

Thin **header helper** for [ContextMemory](https://github.com/Kortexio/ContextMemory) — the OpenAI-compatible agentic memory gateway behind [Kortexio](https://kortexio.io).

This is **not** a full SDK. It only builds the auth / tenant headers (`Authorization`, `X-App-Id`, `X-User-Id`, `X-Session-Id`) so you can keep using the official OpenAI client (or `fetch`).

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
    sessionId: "sess-abc",
  })
);

const completion = await client.chat.completions.create({
  model: "qwen3.5:9b",
  messages: [{ role: "user", content: "Remember that my staging DB is postgres-staging." }],
});

console.log(completion.choices[0]?.message?.content);
```

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

Gateway docs: [GitHub README](https://github.com/Kortexio/ContextMemory) · [compare (why not RAG)](https://github.com/Kortexio/ContextMemory/blob/main/docs/compare.md)

## License

AGPL-3.0-or-later — same as the ContextMemory core.
