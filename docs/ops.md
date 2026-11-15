> Part of the ContextMemory docs. [Back to README](../README.md).

## Persistence

| Component | File | Postgres |
|---|---|---|
| App registry + API keys | ✅ | ✅ |
| Runtime config (LLM, agentic, wiki) | ✅ | ✅ |
| Sessions, messages, session wiki | ✅ | ✅ |
| Global Wiki documents (+ digests / catalog) | ✅ | ✅ |
| MCP catalog + credentials | ✅ | ✅ |
| Agentic skills / guardrail packs | ✅ | ✅ |
| Pending HITL state | ✅ | ✅ |
| Telemetry / rate limits | **in-memory only** (single instance) | **in-memory only** (single instance) |

EF Core migrations live in `ContextMemory.Infrastructure` (includes `global_wiki_documents`, MCP catalog, and agentic policy catalog).

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
- Rotate any credentials that were ever committed to version control

---

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| `docker pull` / health OK but chat hangs or 502/503 | LLM not reachable from the container | Point `ContextMemory__LlmEndpoint` (or legacy `OllamaEndpoint` / `OLLAMA_ENDPOINT`) at a reachable engine. For Ollama on the **host**: `ollama serve` + `ollama pull qwen3.5:9b`, keep `http://host.docker.internal:11434` (Compose already sets `host-gateway`). For vLLM / ExLlamaSharp / LM Studio, set `LLM_ENDPOINT` or per-app `llmBackend=openai-compatible` + `llmEndpoint`. On Linux without Docker Desktop, confirm `host.docker.internal` resolves. |
| Health shows apps/persistence unhealthy | Wrong Master Key path or empty seed | From the API container or host, `curl http://localhost:5100/health` and check `checks.defaultLlmEndpoint`. Override with `-e ContextMemory__LlmEndpoint=...` or `LLM_ENDPOINT` / `OLLAMA_ENDPOINT` in `.env`. |
| Agentic prompt exceeds context (`n_ctx=4096`) | Playground sent `options.num_ctx=4096` (old default) **or** Ollama `/v1` ignored `num_ctx` | Leave Playground `num_ctx` empty (uses app LLM config) or set 8192/32768. Also set `llmOptions.numCtx` on the app (gateway auto-uses `ollama-native`) **or** `OLLAMA_CONTEXT_LENGTH` / Modelfile `PARAMETER num_ctx`. |
| Jinja `No user query found in messages` | Strict Qwen/Bonsai chat_template | Patch model `TEMPLATE` (drop `raise_exception`); gateway already merges compaction into one system + ensures a user message. |
| Model invents Zuora accounts / `0 tool(s)` | Weak model / missing evidence guardrail | Enable `live-data-evidence`; use `harnessMode=weak` or Qwen profile; smoke with `scripts/smoke-multi-model.ps1`. |
| Port already in use (`5100` / `5200`) | Another process bound the port | Change `API_PORT` / `ADMIN_PORT`, or stop the other process / previous container: `docker rm -f contextmemory-api contextmemory-admin`. |
| Admin: “Not connected” / 401 | Missing or wrong Master Key | Settings → Master Key = `ContextMemory:MasterKey` (demo: `cm_master_dev_key_change_me`). For Compose/GHCR Admin, URL from the **Admin container** is `http://api:8080`, not `localhost`. Click **Test connection**. |
| Admin sidebar / menu does nothing | Stale browser cache after upgrade | Hard refresh. Current Admin uses a Blazor toggle (`admin-shell.js`), not AdminLTE jQuery. |
| Chat Lab 401 | Using Master Key as app key | Chat needs the **app** API key (`cm_live_...`) + `X-App-Id`. Master Key is only for Admin APIs. |
| Model not found / empty replies | Model not pulled or name mismatch | `ollama pull <model>` and set the same id in app config / request body (`llmModel` / `"model"`). |
| Custom OpenAI-compatible URL fails | Missing `/v1` or wrong backend | Set backend to `openai-compatible` (or `openai`) and `llmEndpoint` to the server base URL; the gateway appends `/v1` if needed. |
| Data lost after `docker rm` without volume | Ephemeral container FS | Always mount a volume (`-v contextmemory-data:/app/data` or Compose `cm_data`). |

Still stuck? Open a [GitHub issue](https://github.com/Kortexio/ContextMemory/issues) with health JSON (`GET /health`) and sanitized logs.

---

## Tests

```bash
dotnet test tests/ContextMemory.Api.Tests/ContextMemory.Api.Tests.csproj
```

Coverage includes: API contract (OpenAI `/v1` + legacy), session wiki, Global Wiki (query packing, digests), web search, agentic E2E (shell/MCP), skills/guardrails, HITL, streaming, validation, prompt profiles.

---

## Repository structure

```
src/
  ContextMemory.Api/              # HTTP gateway, endpoints, middleware, hosting
  ContextMemory.Core/             # Domain, orchestration, contracts, session wiki logic
  ContextMemory.Infrastructure/   # Persistence, HttpClients, tool executors, telemetry
  ContextMemory.Adapters/         # Ollama, OpenAI, LM Studio, web search
  ContextMemory.Admin.UI/         # Blazor component library (Chat Lab / config editors)
  ContextMemory.Admin.Web/        # Runnable Admin console host (http://localhost:5200)
mcp-runtime/                      # Node host for stdio MCP packages (/opt/mcps)
sandbox-runtime/                  # Node host for self-hosted shell/python/node tools
tests/
  ContextMemory.Api.Tests/        # Integration and E2E tests
```

**Dependency graph:** `Api → Infrastructure → Core` · `Adapters → Core`

Public contracts live in `src/ContextMemory.Core/Contracts/` with XML documentation. Enable Swagger in Development at `/swagger` for the HTTP surface.

---

## Rate limits (single-node)

RPM/TPM (and agentic weight) are enforced **in-process**. That is enough for one API container. **Do not** run multiple API replicas expecting a shared rate-limit budget — counters are not in Postgres. Multi-instance rate limiting is post-1.0.

`GET /metrics` (Prometheus) is the supported ops signal for 0.3.x.

---

## Before you expose the API to the internet

1. Change `ContextMemory__MasterKey` and every app `ApiKey` away from `*_change_me` / demo seeds.
2. Prefer Postgres persistence for sessions/wiki/HITL if you care about durability across restarts.
3. Keep egress defaults; only allowlist hosts you need for HTTP/MCP tools.
4. Enable HITL (`requireConfirmationFor`) for destructive sandbox/MCP actions.
5. Put TLS and auth in front (reverse proxy); do not publish Admin `:5200` without the Master Key locked down.
6. Confirm `GET /health` and one `aha-chat` turn against your real LLM endpoint.

---

## MCP catalog (stdio packaging)

- Run `mcp-runtime` beside the API (Compose service). Place packages under the runtime’s MCP root (Compose: `/opt/mcps`).
- App config: integration `type=mcp`, `enabled=true`, transport HTTP URL to the runtime **or** stdio command registered in the runtime.
- Credentials: `POST /apps/{id}/mcp/credentials/{name}` (app key) or Admin Master Key list/get under `/admin/apps/{id}/mcp/credentials`. Modes: bearer / OAuth fields as exposed in Admin Config — never commit secrets.
- Refresh tools: `POST /apps/{id}/mcp/catalog/rebuild`. Probe: `POST /apps/{id}/mcp/test/{name}`.

---

## Sandbox security model

| Mode | Where code runs | Notes |
|---|---|---|
| **sandbox-runtime** (Compose) | Sibling Node host | Env `SANDBOX_*` on that service; network isolated from the public internet unless you open it |
| **Azure ACA sessions** | Azure Container Apps dynamic sessions | Pool endpoint + credentials in app execution config |

Treat sandbox as **untrusted code execution**. Prefer HITL for shell; keep filesystem mounts read-only where possible; never mount host secrets into the sandbox.

---

## Global Wiki digests (large corpora)

1. Batch upsert: `POST /apps/{id}/wiki/documents/batch`.
2. Rebuild digests + catalog: `POST /apps/{id}/wiki/digests/rebuild` (`force` when content rules change).
3. Agents discover via digests / `wiki:catalog`, then `wiki_search` / `wiki_grep` — do not stuff the full corpus into the prompt.
4. Watch catalog size and `asOf` queries if you supersede often.

---

## Network Compose

`docker-compose.network.yml` adds Postgres + a shared Docker network for multi-service / multi-tenant seeds. Use it when File persistence is not enough:

```bash
docker compose -f docker-compose.yml -f docker-compose.network.yml up --build -d
```

Set `POSTGRES_*` / connection string via env. Seed apps (e.g. demos) still need unique API keys.

---

## Cloud vs OSS

| | **This repo (OSS)** | **Kortexio Cloud** |
|---|---|---|
| Chat API | Self-host `/v1` + `cm_live_…` + `X-App-Id` | Hosted `/v1` + `cmk_live_…` |
| Admin | Blazor on `:5200`, Master Key | Cloud dashboard |
| License | AGPL-3.0 | Commercial terms |
| LLM | BYO endpoint | BYO in dashboard |
| Feature delta | Full gateway source | Hosted ops, billing, no self-host Admin SSO in OSS |

Agentic/HITL `/v1` metadata: see [api.md](api.md).

---

## Engines (memory vs agentic)

| Engine | Session memory (aha) | Agentic tool loop |
|---|---|---|
| llama.cpp (`--jinja`) | Yes (CI uses ~1.5B) | Prefer **≥~7B** instruct + Weak harness |
| vLLM / OpenAI-compatible | Yes | Same — native or client-side tools per profile |
| Ollama | Yes; set `num_ctx` high enough | Qwen/Weak → client-side catalog; watch Jinja templates |

Reports: open a GitHub **engine compatibility** issue with model id + flags.

---

