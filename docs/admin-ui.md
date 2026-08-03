> Part of the ContextMemory docs. [Back to README](../README.md).

## Admin UI guide

The Admin console (`ContextMemory.Admin.Web`, default **http://localhost:5200**) is a Blazor Server app that talks to the ContextMemory API over HTTP. UI copy is English; field help sits under each control.

| | |
|---|---|
| **Who uses it** | Operators configuring tenants (LLM, memory, agentic, MCP, keys) |
| **Auth** | **Master Key** (`ContextMemory:MasterKey`) for admin APIs — not the app `cm_live_…` key |
| **Where settings live** | Browser local storage (overrides host defaults from `Admin__*` / Compose env) |
| **Image** | `ghcr.io/kortexio/contextmemory-admin` |

### Navigation map

| Menu | Route | Purpose |
|---|---|---|
| Applications | `/` | List tenants, open details / config |
| New application | `/apps/new` | Register a tenant and mint an API key |
| Chat Lab | `/chat` | Interactive chat against an app (memory + agentic) |
| Skills | `/skills` | Shared skills catalog (+ view guardrail packs) |
| Settings | `/settings` | API base URL, Master Key, health |

Per-app pages (from Applications):

| Page | Route | Purpose |
|---|---|---|
| Details | `/apps/{appId}` | Telemetry, wiki/web-search stats, view/rotate API key |
| Config | `/apps/{appId}/config` | LLM, wiki, Global Wiki, web search, rate limits, **Agentic Gateway** |

### First-time setup (Settings)

1. Open **Settings**.
2. Set **API base URL**:
   - Local `dotnet run`: `http://localhost:5100`
   - Docker Compose / GHCR Admin container: `http://api:8080` (server-side calls stay on the Docker network; the browser still opens Admin on `localhost:5200`)
3. Paste the **Master Key** (demo: `cm_master_dev_key_change_me`).
4. Click **Save**, then **Test connection**.
5. Confirm Health: Ollama, Persistence, Apps loaded, optional Database.

**Save** stores values in this browser. **Reset to defaults** clears browser overrides and restores host defaults (`Admin__DefaultApiBaseUrl`, `Admin__DefaultMasterKey`). A link to public Prometheus metrics (`GET /metrics`) appears when connected.

### Applications dashboard

- Cards per app: request count, active users, last-turn wiki pages included/total, source badge (`seed` vs registered).
- Actions: **Details**, **Config**, **Chat Lab**.
- **New application** / **Refresh**. Seed app `demo-dev` appears when the API starts with default config.

### Register an application

**New application** creates an isolated tenant (own API key, session wiki, optional Global Wiki).

| Field | Notes |
|---|---|
| Application name | Operator label only (not an HTTP header) |
| Domain | Prefix for generated `X-App-Id` (e.g. `helpdesk` → `helpdesk-…`) |
| Default language | BCP-47 for prompts (`en-US`, `pt-PT`, …) |
| LLM backend / model / endpoint / API key | Same semantics as Config (below) |
| Persona | Optional base system prompt |

After create, **copy the API key immediately** — it is shown once on this screen. Then open Config, Details, or Chat Lab.

### App details (stats + API key)

`/apps/{appId}` shows:

- Requests, errors, active users, average latency
- Prompt/completion tokens, wiki truncated total
- Last-turn wiki memory (chars, pages included/on disk, compaction ok/err)
- Web search totals when used
- **API key** — show/copy; **Generate new API key** rotates it

For **seed** apps (`appsettings`), rotation lasts until API restart unless you also update `ContextMemory:Apps:{appId}:ApiKey`.

Use the app key in Chat Lab and client apps — never the Master Key.

### App Config (runtime)

`/apps/{appId}/config` patches runtime config (`PATCH /admin/apps/{id}/config`). Changes apply to **new requests** after **Save changes**.

#### LLM

| Control | Meaning |
|---|---|
| Default language | Locale in prompts |
| LLM model | Model id on the backend |
| Backend | `ollama` (default `/v1`), `vllm`, `lmstudio`, `openai`, `openai-compatible`, `custom`, `ollama-native` |
| Endpoint / API key | Optional per-app overrides; empty = host defaults |
| Max history messages | Recent turns kept in the prompt (wiki is separate) |
| Streaming enabled | Allow streaming on legacy `/api/chat` |

Prefer clients calling **`POST /v1/chat/completions`**.

#### Session wiki

Max wiki context chars, compaction threshold (bytes), compaction min pages.

#### Global Wiki

- Enable Global Wiki → exposes `wiki_search` for documents under `/apps/{id}/wiki/…`
- Max Global Wiki tool chars — budget per `wiki_search` call (`0` = service default)

Ingest/digests are API-side; the Admin UI toggles availability and budget. Retrieval runs inside the agentic loop — see [Retrieval + agentic loop](#retrieval--agentic-loop).

#### Web search

Enable, mode (`heuristic` / `llm` / `always` / `off`), provider (`tavily` / `brave`), max results, max ephemeral context chars, persist-to-wiki, telemetry logging. Provider API keys live on the **API host** (`ContextMemory:WebSearch`), not per app.

#### Rate limits

App RPM, per-user RPM, TPM, agentic request weight, agentic tokens per iteration.

#### Agentic Gateway (on the Config page)

| Area | What you configure |
|---|---|
| Enable agentic loop | Multi-step tools on the same chat request |
| Prompt profile | `auto` / `ollama` / `openai` / `claude` |
| Loop guardrails | Validation mode, network egress, max iterations, loop timeout, min answer length, confirmation keywords, allowed hosts, expected regexes, require exit 0, human review on max iterations |
| **Skills & guardrail packs** | Per-app checkboxes from the shared catalog (see Skills page). Omit selection → catalog defaults |
| Execution tools | `self-hosted-sandbox` → sandbox endpoint (Compose: `http://sandbox-runtime:8080`) or `aca-session` → ACA pool URL; runtimes shell/python/node/(custom); `allowEgress` |
| MCP integrations | `http` or `stdio`; name, URL/command+args, auth mode, credential ref, OAuth fields, allow/deny tool lists, timeout, enabled, allowEgress; max MCP tools per turn |

Example stdio MCP is shown in a collapsible on the Config page. After editing MCP servers, rebuild the tool catalog via API if needed: `POST /apps/{appId}/mcp/catalog/rebuild` (Master Key or app auth as configured).

#### Persona and rules

Markdown fields: base persona, business rules, format rules, wiki schema.

### Skills catalog

**Skills** (`/skills`) manages the **shared** catalog (all apps). Enabling packs for a tenant is done on that app’s **Config → Agentic → Skills & guardrail packs**.

| Action | Notes |
|---|---|
| New skill / Edit | Id (slug, create-only), name, category, description, prompt markdown, sort order, default-enabled |
| System skills | Prompt editable; cannot delete |
| Download | Export `.skill.json` |
| Import | `.json` / `.md` / `.skill.json`; optional replace-if-exists |
| Guardrail packs table | Read-only list (`id`, `kind`, default); toggle per app in Config |

### Chat Lab

Interactive tester for a tenant. Uses the **app API key** + `X-App-Id` (Master Key only for “Load app” metadata via admin APIs).

**Connection panel**

| Field | Role |
|---|---|
| App ID / User ID / Session ID | Headers; empty session → API generates |
| App API key | Bearer for chat |
| Model | Body `model` (match app config) |
| Chat / Generate | Legacy `POST /api/chat` or `/api/generate` |
| Streaming | NDJSON token stream |
| Show raw JSON | Debug panel |
| Save locally / Load app / New session | Persist lab settings, pull app summary, reset session id |

**Ollama options** — optional per-request system prompt, temperature, top_p, top_k, num_ctx, repeat_penalty, num_predict, keep_alive, format.

**Main pane** — conversation; agentic timeline (tool steps); HITL banner with `confirm` / `[CONFIRM:id]` and copy token; Stop / Regenerate / Clear UI.

Typical smoke path: Settings connected → open Chat Lab → `demo-dev` + demo key → send a message → same Session ID on the next turn to verify memory.

### Keys cheat sheet

| Secret | Used for | Where |
|---|---|---|
| Master Key (`cm_master_…`) | List apps, patch config, rotate keys, skills catalog | Settings |
| App API key (`cm_live_…`) | Chat, wiki ingest, app-scoped MCP ops | Details / Chat Lab / clients |
| LLM API key | Downstream OpenAI-compatible backends | App Config (optional) |
| MCP / OAuth secrets | Integration tools | Config credential ref / OAuth fields (prefer credential store over inline tokens) |

### Admin troubleshooting

| Symptom | Fix |
|---|---|
| Not connected / cannot list apps | Settings → correct API URL + Master Key → Test connection |
| 401 on admin calls | Master Key ≠ `ContextMemory:MasterKey` |
| Chat Lab 401 | Using Master Key as app key — use `cm_live_…` + `X-App-Id` |
| Compose Admin cannot reach API | From Admin container use `http://api:8080`, not `localhost:5100` |
| Sidebar dead after upgrade | Hard refresh (Blazor `admin-shell.js`, not legacy AdminLTE jQuery) |
| Seed key rotation “lost” on restart | Persist new key in `appsettings` / env for seed apps |

---

