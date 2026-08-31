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
| `GET /health` | API, Ollama, Postgres health |
| `GET /admin` | HTML pointer to the Admin UI host |

The preferred chat response is the **OpenAI schema** — `choices[0].message.content`. Legacy `/api/chat` still returns Ollama `message.content` / `done`.

---

