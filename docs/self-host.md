> Part of the ContextMemory docs. [Back to README](../README.md).

## Quick start — self-host

Run the open-source gateway yourself — the path for **on-prem or fully local** deployments. Unlike Cloud (where the dashboard wires up your LLM for you), here **you point the gateway at your own LLM backend** — endpoint and model — in config. Same OpenAI-compatible chat body and `choices[]` response as Cloud; you supply the `X-App-Id` and use a `cm_live_` key.

### Fastest: gateway + bundled engine (Compose)

```bash
git clone https://github.com/Kortexio/ContextMemory.git && cd ContextMemory

# CPU, no GPU needed — llama.cpp pulls a small GGUF on first start
docker compose -f docker-compose.yml -f docker-compose.llamacpp.yml up --build

# or NVIDIA GPU — vLLM
docker compose -f docker-compose.yml -f docker-compose.vllm.yml up --build
```

Then prove memory works (the second request carries only the new question):

```bash
./scripts/aha-chat.sh        # Windows: .\scripts\aha-chat.ps1
```

Engine flags, model choice, and gotchas: [Engines](#engines).

### One-liner from GHCR (API only)

Public images (published on every push to `main`):

| Image | Package |
|---|---|
| `ghcr.io/kortexio/contextmemory` | [API](https://github.com/users/Kortexio/packages/container/package/contextmemory) |
| `ghcr.io/kortexio/contextmemory-admin` | [Admin UI](https://github.com/users/Kortexio/packages/container/package/contextmemory-admin) |

**API only** (needs [Docker](https://docs.docker.com/get-docker/) + any reachable LLM):

**Bring-your-own `/v1` engine** (vLLM, LM Studio, [ExLlamaSharp](https://github.com/Kortexio/ExLlamaSharp), OpenAI, LiteLLM, …):

```bash
docker run --rm -p 5100:8080 \
  --add-host=host.docker.internal:host-gateway \
  -v contextmemory-data:/app/data \
  -e ContextMemory__MasterKey=cm_master_dev_key_change_me \
  -e ContextMemory__Apps__demo-dev__ApiKey=cm_live_dev_key_change_me \
  -e ContextMemory__Apps__demo-dev__LlmBackend=openai-compatible \
  -e ContextMemory__Apps__demo-dev__LlmModel=my-model \
  -e ContextMemory__Apps__demo-dev__LlmEndpoint=http://host.docker.internal:8000 \
  -e ContextMemory__OpenAiEndpoint=http://host.docker.internal:8000 \
  ghcr.io/kortexio/contextmemory:latest
```

**Ollama on the host** (zero-friction DX default):

```bash
docker run --rm -p 5100:8080 \
  --add-host=host.docker.internal:host-gateway \
  -v contextmemory-data:/app/data \
  -e ContextMemory__LlmEndpoint=http://host.docker.internal:11434 \
  -e ContextMemory__MasterKey=cm_master_dev_key_change_me \
  -e ContextMemory__Apps__demo-dev__ApiKey=cm_live_dev_key_change_me \
  -e ContextMemory__Apps__demo-dev__LlmModel=qwen3.5:9b \
  ghcr.io/kortexio/contextmemory:latest
```

`ContextMemory__LlmEndpoint` is the preferred host default; `OllamaEndpoint` remains a legacy alias when `LlmEndpoint` is empty.

Then: http://localhost:5100/health

**API + Admin from GHCR (no local build):**

```bash
docker compose -f docker-compose.ghcr.yml up -d
```

Or the helper scripts:

```bash
./scripts/docker-run.sh --with-admin          # pull from GHCR
./scripts/docker-run.sh --build --with-admin  # build locally instead
```

```powershell
.\scripts\docker-run.ps1 -WithAdmin
.\scripts\docker-run.ps1 -Build -WithAdmin
```

### Build from source: Docker Compose

Builds and starts the **API** (`:5100`), **Admin** (`:5200`), **mcp-runtime** (stdio MCP host), and **sandbox-runtime** (shell/python/node) locally. Requires [Docker](https://docs.docker.com/get-docker/) and an LLM: add a bundled engine override (see [Fastest](#fastest-gateway--bundled-engine-compose)), point `LLM_ENDPOINT` at any `/v1` engine, or fall back to Ollama on the host.

```bash
git clone https://github.com/Kortexio/ContextMemory.git
cd ContextMemory

# optional: customize ports / keys / model
cp .env.example .env

# build + run (File persistence — no Postgres required)
docker compose up --build
```

Then open **[http://localhost:5200](http://localhost:5200)** — Settings are pre-filled for the Compose network (`API base URL` = `http://api:8080`, demo Master Key). Click **Test connection**.

| Service | URL / value |
|---|---|
| API | http://localhost:5100 |
| Admin | http://localhost:5200 |
| Health | http://localhost:5100/health |
| mcp-runtime | internal `http://mcp-runtime:8080` (`ContextMemory__McpRuntimeUrl`) |
| sandbox-runtime | internal self-hosted sandbox for `shell_execute` / `python_execute` / `node_execute` (includes **git** + **Azure CLI** for ops fallback) |

Ops triage (Azure Monitor / GitHub): see [inbound-mcp-guide.md](inbound-mcp-guide.md). Prefer Admin MCP credentials `Env`; Compose `AZURE_*` / `GITHUB_TOKEN` on `sandbox-runtime` is lab-only bootstrap.
| Demo app | `X-App-Id: demo-dev` / Bearer `cm_live_dev_key_change_me` |
| Master key | `cm_master_dev_key_change_me` |

For a Postgres-backed network overlay (shared Docker network, extra tenants), see `docker-compose.network.yml`.

**LLM engine**

```bash
# Bundled: add -f docker-compose.llamacpp.yml (CPU) or -f docker-compose.vllm.yml (GPU)
# Engine on the host: LLM_ENDPOINT=http://host.docker.internal:8080 + Admin backend openai-compatible
# Ollama on the host (fallback when LLM_ENDPOINT is empty): ollama pull qwen3.5:9b
```

**Useful Compose env vars** (see [`.env.example`](../.env.example)):

| Variable | Default | Meaning |
|---|---|---|
| `API_PORT` / `ADMIN_PORT` | `5100` / `5200` | Host ports |
| `LLM_ENDPOINT` | _(empty)_ | Preferred host LLM base URL → `ContextMemory__LlmEndpoint` |
| `OLLAMA_ENDPOINT` | `http://host.docker.internal:11434` | Legacy local default when `LLM_ENDPOINT` is empty |
| `OPENAI_ENDPOINT` / `OPENAI_API_KEY` | OpenAI cloud / empty | Host defaults for `openai-compatible` / `vllm` / `openai` |
| `DEFAULT_LLM_MODEL` | `qwen3.5:9b` | Seed + default model |
| `MASTER_KEY` | `cm_master_dev_key_change_me` | Admin Master Key |
| `DEMO_APP_API_KEY` | `cm_live_dev_key_change_me` | Seed app API key |
| `PERSISTENCE_PROVIDER` | `File` | `File` or `Postgres` |

Stop with `Ctrl+C`, or `docker compose down`. Data for File mode lives in the `cm_data` Docker volume.

**Chat smoke test against the containerized API** (preferred OpenAI route):

```bash
curl -X POST http://localhost:5100/v1/chat/completions \
  -H "Content-Type: application/json" \
  -H "X-App-Id: demo-dev" \
  -H "X-User-Id: user-123" \
  -H "Authorization: Bearer cm_live_dev_key_change_me" \
  -d '{"model":"qwen3.5:9b","messages":[{"role":"user","content":"Hello"}]}'
```

## Engines

The gateway speaks OpenAI-compatible `/v1/chat/completions` to the engine. Any server that implements it works; these are the ones we test and document. Set the app backend to `openai-compatible` (or `vllm`) and the endpoint to the engine base URL — `/v1` is appended automatically.

| Engine | Compose override | Required flags | Notes |
|---|---|---|---|
| **llama.cpp** (`llama-server`) | `docker-compose.llamacpp.yml` | `--jinja` | Without `--jinja`, tool calls come back as plain text and agentic mode degrades. Set `--ctx-size` ≥ 8192 for agentic use. Model name in the request is ignored. |
| **vLLM** | `docker-compose.vllm.yml` | `--enable-auto-tool-choice --tool-call-parser <family>` | Parser must match the model: `hermes` (Qwen2.5 / Qwen3), `llama3_json` (Llama 3.x), `mistral` (Mistral). `--served-model-name` must equal the app `llmModel`. |
| **Ollama** | _(host, default fallback)_ | — | Ollama `/v1` ignores `num_ctx`; when you set `llmOptions.numCtx` the gateway switches to native `/api/chat`. |
| LM Studio, SGLang, TGI, LiteLLM, OpenAI, Azure-compatible | — | tool calling enabled on the server | Point `LLM_ENDPOINT` (host default) or the app `llmEndpoint` at the server. |

**Choosing a model**

| Use | Minimum that works | Recommended |
|---|---|---|
| Session memory only (agentic off — the default) | 1.5B instruct (CI runs `Qwen2.5-1.5B-Instruct Q4_K_M`) | 3B–8B instruct |
| Agentic mode (wiki_search, MCP, sandbox) | 7B–9B instruct with native tool calling | 14B+ or a hosted model |

Small models need the hardening preset in [small-model-guide.md](small-model-guide.md). Found an engine/model combination that works (or does not)? Open an **Engine / model compatibility report** issue.

**Changing the llama.cpp model**

```bash
LLAMACPP_HF_MODEL=bartowski/Qwen2.5-7B-Instruct-GGUF:Q4_K_M \
  docker compose -f docker-compose.yml -f docker-compose.llamacpp.yml up
```

**Engine already running elsewhere** (host, another box, a GPU server):

```bash
LLM_ENDPOINT=http://gpu-box.lan:8000 docker compose up --build
```

Then in **Admin → Config → LLM** set backend `openai-compatible` and the model name the engine expects.

The end-to-end check in CI ([`e2e-llamacpp`](../.github/workflows/e2e-llamacpp.yml)) builds the gateway, starts `llama-server`, and runs `scripts/aha-chat.sh` on every push to `main` and nightly.

## Run from source (dotnet run)

### Prerequisites (dotnet run)

- .NET 9 SDK
- Ollama, [ExLlamaSharp](https://github.com/Kortexio/ExLlamaSharp), vLLM, LM Studio, or any OpenAI-compatible `/v1` backend reachable on the network
- Optional: PostgreSQL 14+ for production / multi-instance HA

### 1. Configure

The committed `appsettings.json` uses **safe placeholders** and `PersistenceProvider: File` (no database required). Seed app id: `demo-dev` with key `cm_live_dev_key_change_me` (change before any real use).

**Local secrets** — choose one approach (never commit real values):

| Method | How |
|---|---|
| User Secrets (recommended) | `cd src/ContextMemory.Api` then `dotnet user-secrets set "ContextMemory:MasterKey" "your-key"` |
| Environment variables | See [`.env.example`](.env.example) for the `__` naming convention |
| Development file | Create `src/ContextMemory.Api/appsettings.Development.json` (gitignored) with local overrides |

For **PostgreSQL** in production or multi-instance HA:

```json
{
  "ConnectionStrings": {
    "ContextMemory": "Host=localhost;Port=5432;Database=contextmemory;Username=...;Password=..."
  },
  "ContextMemory": {
    "PersistenceProvider": "Postgres",
    "DataPath": "../../data",
    "LlmEndpoint": "",
    "OllamaEndpoint": "http://localhost:11434",
    "OpenAiEndpoint": "https://api.openai.com",
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

Use `"Postgres"` exactly (not `Postgresql`). Relative `DataPath` values resolve from the API content root.

#### `appsettings.json` field reference

| Field | Meaning |
|---|---|
| `ConnectionStrings:ContextMemory` | PostgreSQL connection string when `PersistenceProvider` is `Postgres` |
| `ContextMemory:PersistenceProvider` | `File` (default) or `Postgres` |
| `ContextMemory:DataPath` | Root for file-based persistence (apps, sessions, wiki) |
| `ContextMemory:LlmEndpoint` | Preferred host default LLM base URL (any OpenAI- or Ollama-compatible engine). Empty = use `OllamaEndpoint` |
| `ContextMemory:OllamaEndpoint` | Legacy alias for the local/Ollama-compatible default when `LlmEndpoint` is empty |
| `ContextMemory:OpenAiEndpoint` | Host default for `openai` / `openai-compatible` / `vllm` / `custom` when the app has no `llmEndpoint` |
| `ContextMemory:LmStudioEndpoint` | Host default for `lmstudio` |
| `ContextMemory:DefaultLlmModel` | Fallback model when an app has no `LlmModel` |
| `ContextMemory:MasterKey` | Secret for Admin API / admin dashboard |
| `ContextMemory:AdminCorsOrigins` | Allowed browser origins for Admin UI CORS |
| `ContextMemory:WebSearch:*` | Tavily/Brave keys, default provider, timeout |
| `ContextMemory:Apps` | Seed map of tenant apps; each key is the **app id** (`X-App-Id`) |

Per-app runtime settings (agentic tools, wiki schema, web-search toggles, guardrails) are managed via the admin API or Admin UI host, not only this seed section.

### 2. Start the API

```bash
cd src/ContextMemory.Api
dotnet run
```

API defaults to `http://localhost:5100` (Swagger in Development at `/swagger`).

### 3. First chat request

```bash
curl -X POST http://localhost:5100/v1/chat/completions \
  -H "Content-Type: application/json" \
  -H "X-App-Id: demo-dev" \
  -H "X-User-Id: user-123" \
  -H "X-Session-Id: sess-abc" \
  -H "Authorization: Bearer cm_live_dev_key_change_me" \
  -d '{
    "model": "qwen3.5:9b",
    "messages": [{ "role": "user", "content": "Hi, do you remember my name?" }]
  }'
```

**Response (OpenAI schema — identical to Cloud):**

```json
{
  "id": "chatcmpl-...",
  "object": "chat.completion",
  "model": "qwen3.5:9b",
  "choices": [{
    "index": 0,
    "message": { "role": "assistant", "content": "..." },
    "finish_reason": "stop"
  }]
}
```

Legacy `POST /api/chat` still returns the Ollama `message` / `done` shape (deprecated).

**Required headers:** `X-App-Id`, `X-User-Id`, `Authorization: Bearer {API_KEY}`  
**Optional:** `X-Session-Id` (generated by the API if omitted).

### 4. Admin UI and Chat Lab

Prefer Docker Compose above, or run the Admin host locally against a local API:

```bash
cd src/ContextMemory.Admin.Web
dotnet run
```

Open **[http://localhost:5200](http://localhost:5200)** → **Settings** → set API URL `http://localhost:5100` and your Master Key (`ContextMemory:MasterKey`) → **Test connection**.

Full walkthrough of every screen: **[Admin UI guide](admin-ui.md)**.

The API also serves a short HTML pointer at [http://localhost:5100/admin](http://localhost:5100/admin). Admin HTTP APIs still require the Master Key bearer token.

---

