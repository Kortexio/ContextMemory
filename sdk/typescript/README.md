# @kortexio/contextmemory (TypeScript)

Thin header helper for the ContextMemory gateway (not a full SDK).

```bash
npm i @kortexio/contextmemory
```

```ts
import OpenAI from "openai";
import { openAIClientOptions } from "@kortexio/contextmemory";

const client = new OpenAI(
  openAIClientOptions({
    baseUrl: "http://localhost:5100/v1",
    apiKey: "cm_live_...",
    appId: "demo-dev",
    userId: "user-42",
    sessionId: "sess-abc",
  })
);
```
