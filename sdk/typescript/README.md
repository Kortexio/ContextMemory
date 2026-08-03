# contextmemory (TypeScript)

Thin helper for gateway headers.

```bash
npm i ./sdk/typescript
```

```ts
import OpenAI from "openai";
import { openAIClientOptions } from "contextmemory";

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
