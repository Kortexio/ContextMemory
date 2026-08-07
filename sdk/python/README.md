# kortexio-contextmemory

Thin **header helper** for [ContextMemory](https://github.com/Kortexio/ContextMemory) — the OpenAI-compatible agentic memory gateway behind [Kortexio](https://kortexio.io).

This is **not** a full SDK. It only builds the auth / tenant headers (`Authorization`, `X-App-Id`, `X-User-Id`, `X-Session-Id`) so you can keep using the official OpenAI Python client (or `httpx` / `requests`).

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
        session_id="sess-abc",
    )
)

completion = client.chat.completions.create(
    model="qwen3.5:9b",
    messages=[{"role": "user", "content": "Remember that my staging DB is postgres-staging."}],
)
print(completion.choices[0].message.content)
```

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

Gateway docs: [GitHub README](https://github.com/Kortexio/ContextMemory) · [compare (why not RAG)](https://github.com/Kortexio/ContextMemory/blob/main/docs/compare.md)

## License

AGPL-3.0-or-later — same as the ContextMemory core.
