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
| Telemetry / rate limits | in-memory | in-memory |

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
| `docker pull` / health OK but chat hangs or 502/503 | Ollama not reachable from the container | On the **host**: `ollama serve` + `ollama pull qwen3.5:9b`. Keep `ContextMemory__OllamaEndpoint=http://host.docker.internal:11434` (Compose/scripts already set `host-gateway`). On Linux without Docker Desktop, confirm `host.docker.internal` resolves. |
| Health shows Ollama unhealthy | Wrong endpoint or firewall | From the API container or host, `curl http://host.docker.internal:11434/api/tags`. Override with `-e ContextMemory__OllamaEndpoint=...` or `OLLAMA_ENDPOINT` in `.env`. |
| Agentic prompt exceeds context (`n_ctx=4096`) | Ollama `/v1` ignored `num_ctx` | Set `llmOptions.numCtx` (gateway auto-uses `ollama-native`) **or** `OLLAMA_CONTEXT_LENGTH` / Modelfile `PARAMETER num_ctx`. |
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

## Documentation backlog

Items still thin or missing from this README (code exists; docs TBD or expand later):

| Area | What's missing |
|---|---|
| **MCP catalog** | End-to-end stdio packaging guide (`mcp-runtime` + `/opt/mcps`) and credential modes matrix beyond Config field help |
| **sandbox-runtime** | Security model, env knobs (`SANDBOX_*`), and ACA vs self-hosted comparison beyond the feature bullets |
| **Global Wiki digests** | Operational guidance for large corpora (batch ingest → `digests/rebuild`, when to `force`, catalog size) |
| **Network Compose** | `docker-compose.network.yml` (Postgres, shared network, multi-tenant seeds such as `laravox` / `kyc`) as a first-class quick-start path |
| **OpenAI metadata** | Where agentic progress / HITL confirmation appear on `/v1` responses vs legacy `/api/chat` `context_memory` |
| **Cloud vs OSS deltas** | Explicit matrix of Cloud-only dashboard features vs this repo |

PRs that close any row above are welcome — keep examples in English and prefer `/v1/chat/completions` in new snippets.

---

