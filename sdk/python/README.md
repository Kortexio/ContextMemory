# kortexio-contextmemory

## What is ContextMemory?

[ContextMemory](https://github.com/Kortexio/ContextMemory) is an **agentic memory gateway**. Your coding agent or chat app talks to a normal OpenAI-compatible URL (`POST /v1/chat/completions`), and the gateway keeps **markdown memory you can open like a wiki** — plus tools, sandbox, MCP, and human-in-the-loop when you need action.

It is **not** classic RAG (no “inject N chunks into the prompt”). Memory is retrieved on demand inside the agent loop (`wiki_search` and related tools). It is also **not** a vector black box: facts live as files you can read, edit, and version.

Use it when:

- Cursor / Claude / your agent **forgets** staging names, decisions, and project context between sessions  
- You want **auditable** memory (markdown) instead of opaque embeddings  
- You want **one `/v1` URL** for chat + memory + tools, self-hosted or on [Kortexio Cloud](https://kortexio.io)

## What is this package?

A **thin header helper** for Python — **not** a full SDK.

ContextMemory needs a few HTTP headers on every call (`Authorization`, and usually `X-App-Id` / `X-User-Id` / `X-Session-Id`). This package builds those headers (and kwargs for the official OpenAI client) so you do not hand-roll them.

> Beta (`0.0.1b*`). APIs may still change.  
> **PyPI name:** `kortexio-contextmemory` · **Import name:** `contextmemory`

## Install

```bash
pip install kortexio-contextmemory openai
```

## Quick start (OpenAI Python client)

Point the client at your gateway `/v1` base URL (self-host or Cloud):

```python
import os
from openai import OpenAI
from contextmemory import openai_client_kwargs

client = OpenAI(
    **openai_client_kwargs(
        base_url=os.environ.get("CONTEXTMEMORY_BASE_URL", "http://localhost:5100/v1"),
        api_key=os.environ["CONTEXTMEMORY_API_KEY"],  # cm_live_… or self-host key
        app_id="demo-dev",
        user_id="user-42",
        session_id="sess-abc",  # same session → same wiki memory
    )
)

completion = client.chat.completions.create(
    model="qwen3.5:9b",
    messages=[{"role": "user", "content": "Remember that my staging DB is postgres-staging."}],
)
print(completion.choices[0].message.content)
```

Reuse the same `session_id` across turns so the gateway attaches the right session wiki.

## Headers only (`httpx`, `requests`)

```python
import httpx
from contextmemory import headers

h = headers(
    api_key="cm_live_...",
    app_id="demo-dev",
    user_id="user-42",
    session_id="sess-abc",
)

r = httpx.post(
    "http://localhost:5100/v1/chat/completions",
    headers=h,
    json={
        "model": "qwen3.5:9b",
        "messages": [{"role": "user", "content": "Hello"}],
    },
)
print(r.json())
```

## API

| Function | Purpose |
| --- | --- |
| `openai_client_kwargs(...)` | `dict` for `OpenAI(**kwargs)` / `AsyncOpenAI(**kwargs)` |
| `headers(...)` | Plain `dict[str, str]` for any HTTP client |

**Required:** `api_key` (and `base_url` for the OpenAI helper)  
**Optional:** `app_id`, `user_id`, `session_id`, `extra` (merged into headers)

## Cloud vs self-host

| | Base URL | API key |
| --- | --- | --- |
| **Self-host** | `http://localhost:5100/v1` (or your host) | App key from Admin / config |
| **[Kortexio Cloud](https://kortexio.io)** | Cloud API `/v1` | `cmk_live_…` |

More: [GitHub](https://github.com/Kortexio/ContextMemory) · [why we are not RAG](https://github.com/Kortexio/ContextMemory/blob/main/docs/compare.md) · [MCP aha demo](https://github.com/Kortexio/ContextMemory/blob/main/docs/aha-demo.html)

## License

AGPL-3.0-or-later — same as the ContextMemory core.
