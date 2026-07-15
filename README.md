# ContextMemory Agentic Gateway

**Give an LLM memory by swapping a URL. That same URL can also act when it needs to.**

ContextMemory is a context and agent proxy for applications that already talk to LLMs through an Ollama/OpenAI-compatible API. Your integration code **does not change**: you keep using `POST /api/chat` with the same headers and message schema. Behind the scenes, the gateway enriches each turn with session memory (wiki), optional web search, and — when enabled — an **agentic loop** with tools isolated per tenant.

---

## Why it exists

| Problem | ContextMemory solution |
|---|---|
| The LLM forgets context between messages | Per-session compiled wiki + recent history injected automatically |
| You need actions (shell, APIs, MCP) without a new endpoint | Agentic loop on the same `/api/chat`, invisible to the client |
| Each client/tenant needs different tools and rules | Per-app configuration: ACA, MCP, guardrails, prompts |
| Destructive actions need human control | Blocking human-in-the-loop with wiki checkpoints |
| Streaming with multi-step loops is complex | Internal buffer: client only receives final text; optional progress via metadata |

---

## Architecture in 30 seconds

```
Your app (zero contract changes)
        │
        ▼ POST /api/chat  (Ollama-compatible)
┌───────────────────────────────────────────┐
│  ContextMemory Gateway (.NET 9)           │
│  1. Auth + tenant (X-App-Id, API key)     │
│  2. Memory: history + session wiki        │
│  3. Web search (optional)                 │
│  4. Agentic loop (if enabled)             │
│     LLM ↔ tools ↔ validation ↔ HITL       │
│  5. Response in the schema you already use │
└───────────┬───────────────┬───────────────┘
            ▼               ▼
     ACA Dynamic        MCP Servers
     Sessions           (Zuora, Jira, …)
     shell/python/      JSON-RPC + OAuth
     node/custom
```

---

## Features

### Contextual memory (core)

- **Per-session wiki** — Markdown pages, index, execution log, and schema; automatic compaction as volume grows.
- **Recent history** — last N messages injected into the prompt (configurable per tenant).
- **Persona and rules** — `basePersona`, `businessRules`, `formatRules`, `wikiSchema` per application.
- **Zero client changes** — send only the new message; middleware builds the full prompt.

### Agentic Gateway (Blueprint v0.2)

- **Same endpoint** — `POST /api/chat`; enabled via `agentic.enabled` in tenant config.
- **Orchestrator** — loop with iteration cap, configurable timeout, and validation before returning the final answer.
- **Execution tools (ACA Dynamic Sessions)**
  - `shell_execute` — isolated shell commands
  - `python_execute` — Python code
  - `node_execute` — Node.js code
  - `container_execute` — **custom** runtime with a dedicated container image
- **Integration tools (MCP)**
  - Dynamic catalog per configured MCP server
  - Qualified naming `server__tool`
  - `mcp_servers` pass-through for backends that support it natively
  - Authentication: bearer, api-key, or **per-tenant OAuth client-credentials**
- **Per-tenant guardrails**
  - `maxIterations`, `loopTimeoutSeconds`
  - `requireConfirmationFor` — keywords that trigger HITL **before** execution
  - `networkEgress: restricted` — blocks external endpoints except allowlist/`allowEgress`
  - `validationMode`: `deterministic` | `hybrid` | `llm-judge`
  - `requireZeroExitCode`, `expectedAnswerPatterns` (regex), `blockedAnswerPatterns`
  - `humanReviewOnMaxIterations` — human review when the loop exhausts iterations
- **Human-in-the-loop**
  - Blocks before destructive tools; state persisted per session
  - Response includes `[CONFIRM:id]`; user confirms with natural language or explicit token
  - Checkpoint in session wiki `log.md` (`agentic-checkpoint`)
  - Human review of partial answers when max iterations is reached
- **Prompt profiles** — `auto`, `ollama`, `openai`, `claude` for system prompts, tool descriptions, and observations tuned to backend/model.

### Streaming and latency

- With `stream: true`, tool calls and internal observations **do not leak** into the text stream.
- The final answer is emitted in chunks after the loop completes (or times out).
- Agentic progress metadata via `context_memory.agentic` (timeline in Chat Lab).
- Timeout with graceful partial response + header `X-Context-Memory-Agentic-Timed-Out`.

### Web search (v3.2)

- Providers: **Tavily**, **Brave**
- Modes: `heuristic`, `llm`, `always`, `off`
- Ephemeral context in the prompt; optional persistence of facts to the wiki
- Response headers: `X-Web-Search-Used`, `X-Web-Search-Provider`, etc.

### Multi-tenant and operations

- **Isolated apps** — API key, config, tools, and guardrails per `appId`
- **Rate limiting** — RPM/TPM per app and per user; extra weight for agentic turns (`agenticRequestWeight`, `agenticTokensPerIteration`)
- **Telemetry** — requests, tokens, latency, wiki, web search, active users
- **Admin UI** — dashboard, app registration, runtime config, **Chat Lab** with agentic timeline and human confirmation UI
- **Persistence**
  - `File` — development and single-node
  - `Postgres` — apps, profiles, **sessions/wiki**, and HITL state in JSONB (multi-instance HA)

### Supported LLM backends

- **Ollama** (default)
- **OpenAI-compatible** (OpenAI, Azure OpenAI, etc.)
- **LM Studio**

---

## Quick start

### Prerequisites

- .NET 9 SDK
- Ollama (or another configured backend) reachable on the network
- Optional: PostgreSQL 14+ for production

### 1. Configure

The committed `appsettings.json` uses **safe placeholders** and `PersistenceProvider: File` (no database required).

**Local secrets** — choose one approach (never commit real values):

| Method | How |
|---|---|
| User Secrets (recommended) | `cd src/ContextMemory.Api` then `dotnet user-secrets set "ContextMemory:MasterKey" "your-key"` |
| Development file | Copy `appsettings.Development.example.json` → `appsettings.Development.json` and fill in values (gitignored) |
| Environment variables | See `.env.example` for the `__` naming convention |

For **PostgreSQL** in production or multi-instance HA:

```json
{
  "ConnectionStrings": {
    "ContextMemory": "Host=localhost;Port=5432;Database=contextmemory;Username=...;Password=..."
  },
  "ContextMemory": {
    "PersistenceProvider": "Postgres",
    "DataPath": "./data",
    "OllamaEndpoint": "http://localhost:11434",
    "MasterKey": "your-master-key",
    "Apps": {
      "my-app": {
        "ApiKey": "cm_live_...",
        "SystemPrompt": "You are a helpful assistant.",
        "LlmModel": "qwen3.5:9b"
      }
    }
  }
}
```

#### `appsettings.json` field reference

Values below match the committed template in `src/ContextMemory.Api/appsettings.json`. Prefer User Secrets / environment variables for keys and connection strings.

| Field | Meaning |
|---|---|
| `Logging:LogLevel:Default` | Default log verbosity for the host (e.g. `Information`). |
| `Logging:LogLevel:Microsoft.AspNetCore` | Verbosity for ASP.NET Core framework logs (often `Warning` to reduce noise). |
| `Logging:LogLevel:ContextMemory` | Verbosity for gateway application logs under the `ContextMemory` category. |
| `AllowedHosts` | Host-filtering allow-list. `"*"` accepts any host header (fine for local/dev). |
| `ConnectionStrings:ContextMemory` | PostgreSQL connection string. Required when `PersistenceProvider` is `Postgres`; ignored when using `File`. |
| `ContextMemory:PersistenceProvider` | Storage backend: `File` (session wiki/profiles under `DataPath`) or `Postgres` (EF Core + migrate on startup). Default: `File`. |
| `ContextMemory:DataPath` | Root directory for file-based persistence (apps, sessions, wiki). Relative paths are resolved from the API content root. |
| `ContextMemory:OllamaEndpoint` | Base URL of the default Ollama (or Ollama-compatible) LLM backend. |
| `ContextMemory:DefaultLlmModel` | Fallback model id when an app does not set its own `LlmModel` (e.g. at registration). |
| `ContextMemory:MasterKey` | Secret for Admin API / Admin UI. Sent as the admin bearer/master key; **never commit a production value**. |
| `ContextMemory:AdminCorsOrigins` | Allowed browser origins for CORS policy `AdminWebCors` (Admin UI + Chat Lab). Empty list disables that policy’s origins. |
| `ContextMemory:WebSearch:TavilyApiKey` | API key for the Tavily web-search provider. Empty = Tavily unavailable. |
| `ContextMemory:WebSearch:BraveApiKey` | API key for the Brave Search provider. Empty = Brave unavailable. |
| `ContextMemory:WebSearch:DefaultProvider` | Preferred web-search provider when more than one is configured (`tavily` or `brave`). |
| `ContextMemory:WebSearch:RequestTimeoutSeconds` | HTTP timeout (seconds) for outbound web-search provider calls. |
| `ContextMemory:Apps` | Seed map of tenant apps loaded at startup. Each key is the **app id** (`X-App-Id`). |
| `ContextMemory:Apps.{appId}:ApiKey` | Tenant API key for `Authorization: Bearer …` on chat/generate. |
| `ContextMemory:Apps.{appId}:SystemPrompt` | Base system persona for that app (merged into the enriched prompt with wiki/web sections). |
| `ContextMemory:Apps.{appId}:DefaultLanguage` | Tenant locale (BCP-47), e.g. `en-US` or `pt-PT`. Selects English/Portuguese copy for LLM prompts, HITL text, and wiki tool messages. |
| `ContextMemory:Apps.{appId}:LlmModel` | Default model for that app’s chat/generate turns (overrides `DefaultLlmModel` for the tenant). |

Additional options exist on `ContextMemoryOptions` / `WebSearchOptions` (timeouts, LM Studio/OpenAI endpoints, rate-limit defaults, wiki budgets, etc.) but are not present in the committed `appsettings.json`; set them in Development config, env vars, or User Secrets when needed. Per-app runtime settings (agentic tools, wiki schema, web-search toggles, guardrails) are managed under **Apps → Config** in the Admin UI (or the apps config API), not only in this seed section.

### 2. Start the API

```bash
cd src/ContextMemory.Api
dotnet run
```

API defaults to `http://localhost:5100`.

### 3. First chat request

```bash
curl -X POST http://localhost:5100/api/chat \
  -H "Content-Type: application/json" \
  -H "X-App-Id: my-app" \
  -H "X-User-Id: user-123" \
  -H "X-Session-Id: sess-abc" \
  -H "Authorization: Bearer cm_live_..." \
  -d '{
    "model": "qwen3.5:9b",
    "messages": [{ "role": "user", "content": "Hi, do you remember my name?" }]
  }'
```

**Required headers:** `X-App-Id`, `X-User-Id`, `Authorization: Bearer {API_KEY}`  
**Optional:** `X-Session-Id` (generated by the API if omitted)

### 4. Admin UI and Chat Lab

```bash
cd src/ContextMemory.Admin.UI
dotnet run
```

- **Settings** — API URL + Master Key
- **Apps → Config** — LLM, wiki, web search, rate limits, **Agentic Gateway**
- **Chat Lab** — test streaming, agentic timeline, human confirmation

---

## Agentic configuration (example)

```json
{
  "agentic": {
    "enabled": true,
    "promptProfile": "auto",
    "tools": {
      "execution": [
        { "type": "aca-session", "runtime": "shell", "poolEndpoint": "https://pool.eastus.dynamicsessions.io/..." },
        { "type": "aca-session", "runtime": "python", "poolEndpoint": "https://pool.eastus.dynamicsessions.io/..." },
        { "type": "aca-session", "runtime": "custom", "poolEndpoint": "https://pool.eastus.dynamicsessions.io/...", "containerImage": "myregistry.azurecr.io/agent-tools:1.0" }
      ],
      "integrations": [
        {
          "type": "mcp",
          "name": "zuora-mcp",
          "url": "https://internal/zuora-mcp",
          "authMode": "oauth-per-tenant",
          "allowEgress": true,
          "oauth": {
            "tokenUrl": "https://login.example.com/oauth/token",
            "clientId": "client-id",
            "clientSecret": "client-secret",
            "scope": "mcp.read"
          }
        }
      ]
    },
    "guardrails": {
      "maxIterations": 15,
      "loopTimeoutSeconds": 120,
      "validationMode": "hybrid",
      "requireConfirmationFor": ["delete", "deploy-prod"],
      "networkEgress": "restricted",
      "allowedEgressHosts": ["internal.example.com"],
      "requireZeroExitCode": true,
      "expectedAnswerPatterns": ["^## Summary"],
      "humanReviewOnMaxIterations": true
    }
  }
}
```

Configure via Admin UI (`/apps/{appId}/config`) or `PATCH /admin/apps/{appId}/config` with the Master Key.

---

## Human-in-the-loop — how it works

1. The model proposes a tool that matches `requireConfirmationFor` (e.g. a command containing `delete`).
2. Execution **stops**; the API returns a confirmation prompt and header `X-Context-Memory-Agentic-Awaiting-Confirmation`.
3. The user replies with confirmation (e.g. `confirm`, `approve`, or `[CONFIRM:abc123]`).
4. The tool runs; the loop continues until a validated final answer.
5. If the iteration limit is reached, `humanReviewOnMaxIterations` requests **approval of the partial answer**.

Everything is recorded in the session `log.md` for audit.

---

## API — stable contract

| Endpoint | Description |
|---|---|
| `POST /api/chat` | Ollama-compatible chat (+ agentic, memory, web search) |
| `POST /api/generate` | Ollama-compatible completion |
| `GET /apps/{id}/config` | Runtime config (auth with app API key) |
| `PATCH /admin/apps/{id}/config` | Update config (Master Key) |
| `GET /health` | API, Ollama, Postgres health |

**Chat response** — unchanged Ollama schema; optional extensions in `context_memory`:

```json
{
  "model": "qwen3.5:9b",
  "message": { "role": "assistant", "content": "..." },
  "done": true,
  "context_memory": {
    "message_id": "...",
    "agentic": {
      "phase": "Completed",
      "awaiting_confirmation": false,
      "steps": [{ "tool_name": "shell_execute", "success": true }]
    }
  }
}
```

---

## Persistence

| Component | File | Postgres |
|---|---|---|
| App registry + API keys | ✅ | ✅ |
| Runtime config (LLM, agentic, wiki) | ✅ | ✅ |
| Sessions, messages, wiki | ✅ | ✅ |
| Pending HITL state | ✅ | ✅ |
| Telemetry / rate limits | in-memory | in-memory |

EF Core ships a single `InitialCreate` migration in `ContextMemory.Infrastructure` (current schema: 4 Postgres tables).

```bash
dotnet ef database update \
  --project src/ContextMemory.Infrastructure \
  --startup-project src/ContextMemory.Api
```

To add a new migration:

```bash
dotnet ef migrations add <MigrationName> \
  --project src/ContextMemory.Infrastructure \
  --startup-project src/ContextMemory.Api
```

---

## Language and localization

| Layer | Language |
|---|---|
| Source code, logs, HTTP errors, Admin UI | **English** |
| README, `.env.example`, config templates | **English** |
| LLM prompts, wiki schema, HITL keywords, tool outputs to the model | **Tenant locale** (`DefaultLanguage`, `WikiSchema`, `BasePersona`) |

The seed app in `appsettings.json` uses `en-US`. Tenants can set `DefaultLanguage` to `pt-PT` (or any BCP-47 tag) and customize `WikiSchema` / personas for localized assistant behavior. Session wiki defaults are in `SessionDefaults.cs`; override per tenant via runtime config.

---

## Security and compliance

- Full tool isolation per tenant
- Restricted egress by default; explicit exceptions per tool/host
- Mandatory HITL for configurable destructive actions
- Append-only wiki log per session (agentic checkpoints)
- Rate limits with extra cost accounting for agentic loops
- **Do not commit real credentials** — `appsettings.Development.json`, `.env`, and `data/` are gitignored; use User Secrets, environment variables, or Key Vault in production
- Rotate any credentials that were ever committed to version control before publishing

---

## Tests

```bash
dotnet test tests/ContextMemory.Api.Tests/ContextMemory.Api.Tests.csproj
```

**124 tests** covering: API contract, wiki, web search, agentic E2E (shell/MCP), HITL, streaming, guardrails, validation, prompt profiles.

---

## Repository structure

```
src/
  ContextMemory.Api/              # HTTP gateway, endpoints, middleware, hosting
  ContextMemory.Core/             # Domain, orchestration, contracts, session wiki logic
  ContextMemory.Infrastructure/   # Persistence, HttpClients, tool executors, telemetry
  ContextMemory.Adapters/         # Ollama, OpenAI, LM Studio, web search
  ContextMemory.Admin.UI/         # Operations console + Chat Lab
tests/
  ContextMemory.Api.Tests/        # Integration and E2E tests
```

**Dependency graph:** `Api → Infrastructure → Core` · `Adapters → Core`

Public contracts live in `src/ContextMemory.Core/Contracts/` with XML documentation. Enable Swagger in Development at `/swagger` for the HTTP surface.

---

## Public roadmap — current status

| Phase | Scope | Status |
|---|---|---|
| 1 | Orchestrator + validator + ACA shell + wiki + flag | ✅ |
| 2 | Per-tenant configurable MCP | ✅ |
| 3 | Streaming with buffer + partial timeout | ✅ |
| 4 | Hybrid validator + guardrails + HITL | ✅ |
| 5 | Prompt profiles per model | ✅ |

**Ready for public release** with documentation, full Admin UI, Postgres HA, OAuth MCP, custom containers, and test suite.

---

## Licensing

Define the license before publishing the repository. The historical core may use AGPL-3.0; the Agentic Gateway may adopt a different license depending on commercial positioning.

---

## Support and contribution

See [CONTRIBUTING.md](CONTRIBUTING.md) for language policy, local setup, and PR guidelines.

To report issues or propose improvements, open a GitHub issue after the repository is published. For enterprise integration (ACA pools, internal MCP, admin SSO), contact the commercial team.

---

*ContextMemory — one URL for memory and action.*
