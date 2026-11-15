> Part of the ContextMemory docs. [Back to README](../README.md).

## API — stable contract

| Endpoint | Description |
|---|---|
| `POST /v1/chat/completions` | **Preferred** OpenAI-compatible chat (+ agentic, session memory, Global Wiki tool, web search) |
| `GET /v1/models` | OpenAI-compatible model list for the tenant |
| `POST /api/chat` | Deprecated Ollama-compatible chat |
| `POST /api/generate` | Deprecated Ollama-compatible generate |
| `PUT /apps/{id}/wiki/documents/{documentId}` | Upsert Global Wiki document (storage-only; default supersede on content change) |
| `POST /apps/{id}/wiki/documents/batch` | Batch upsert Global Wiki documents |
| `POST /apps/{id}/wiki/digests/rebuild` | Rebuild LLM digests + `wiki:catalog` |
| `GET /apps/{id}/wiki/documents/{documentId}` | Get active Global Wiki document |
| `GET /apps/{id}/wiki/documents/{documentId}/revisions` | Revision timeline for a document |
| `GET /apps/{id}/wiki/documents` | List Global Wiki documents (`includeSuperseded` optional) |
| `GET /apps/{id}/wiki/audit` | Export wiki revisions (`from` / `to` optional) |
| `DELETE /apps/{id}/wiki/documents/{documentId}` | Soft-delete active revision (closes validity window) |
| `POST /apps/{id}/wiki/query` | Search Global Wiki (`asOf` for point-in-time) |
| `GET /apps/{id}/sessions/{userId}/{sessionId}/wiki` | Compiled session wiki recall |
| `GET /apps/{id}/mcp/servers` | List MCP servers / catalog status for the app |
| `POST /apps/{id}/mcp/catalog/rebuild` | Refresh MCP tool catalog (HTTP + stdio) |
| `POST /apps/{id}/mcp/test/{name}` | Probe an MCP server |
| `POST /apps/{id}/mcp/credentials/{name}` | Upsert MCP credentials |
| `GET /admin/apps/{id}/mcp/credentials` | List stored MCP secrets (Master Key; values unmasked) |
| `GET /admin/apps/{id}/mcp/credentials/{name}` | MCP secrets for one integration (Master Key) |
| `GET /apps/{id}/config` | Runtime config (auth with app API key) |
| `PATCH /admin/apps/{id}/config` | Update config (Master Key), including `GlobalWikiEnabled` |
| `GET /admin/agentic/catalog` | Platform skills + guardrail packs |
| `POST/PUT/DELETE /admin/agentic/skills...` | Platform skills (import/export) |
| `POST/PUT/DELETE /admin/agentic/guardrails...` | Platform guardrails (import/export) |
| `GET /admin/apps/{appId}/agentic/catalog` | App-owned skills + guardrails |
| `POST/PUT/DELETE /admin/apps/{appId}/skills...` | App skills CRUD (import/export) |
| `POST/PUT/DELETE /admin/apps/{appId}/guardrails...` | App guardrails CRUD (import/export) |
| `GET /health` | API and Postgres liveness |
| `GET /admin` | HTML pointer to the Admin UI host |

The preferred chat response is the **OpenAI schema** — `choices[0].message.content`. Legacy `/api/chat` still returns Ollama `message.content` / `done`.

### Stability (0.3.x-beta freeze)

| Surface | Status |
|---|---|
| `POST /v1/chat/completions` + tenant headers (`Authorization`, `X-App-Id`, `X-User-Id`, `X-Session-Id`) | **Stable** |
| OpenAI response shape (`choices`, streaming SSE chunks, `[DONE]`) | **Stable** |
| Agentic / HITL metadata on `/v1` (see below) | **Stable** |
| Admin HTTP under `/admin/...` (Master Key) — apps, config, agentic catalog, MCP credentials | **Stable** |
| App-scoped wiki / MCP / session wiki routes in the table above | **Stable** |
| Admin **Blazor UI** (layout, components) | Not a versioned API |
| Harness knobs (`harnessMode`, experimental profiles) | May tighten; defaults stay Weak-friendly |

Breaking changes to **Stable** rows require a major bump after GA; during beta we still avoid them without a CHANGELOG note.

### Agentic / HITL on `/v1`

**Response headers (non-stream and stream):**

| Header | Meaning |
|---|---|
| `X-Context-Memory-Agentic: true` | Agentic loop ran (or is enabled for the stream) |
| `X-Context-Memory-Agentic-Awaiting-Confirmation: true` | HITL pause — reply with approve / `[CONFIRM:id]` |
| `X-Context-Memory-Agentic-Timed-Out: true` | Loop hit timeout |
| `X-Context-Memory-Agentic-Progress: true` | Stream path advertises progress support |
| `X-Context-Memory-Message-Id` | Reserved id for the streamed turn (when set) |

**Body metadata:** OpenAI-compatible payloads may include `context_memory.agentic` (`AgenticStreamMetadata`): `phase`, `iteration` / `iterations`, `tool_name`, `detail`, `label`, `steps`, `awaiting_confirmation`, `pending_confirmation_id`, `timed_out`, `max_iterations_reached`.

On **SSE** streams, tool progress is not emitted as mid-loop content chunks today — the final SSE chunk carries `context_memory.agentic` after the loop finishes. Confirm HITL via the awaiting-confirmation header + message content (see [hitl.md](hitl.md)).

Legacy `/api/chat` may mirror the same `context_memory` object; prefer `/v1` for new clients.

Optional npm/PyPI **header helpers** only build auth/tenant headers — they are not a client API. Contract = `/v1`.

---

