# contextmemory (Python)

Thin helper so you do not hand-roll `X-App-Id` / `X-Session-Id` headers.

```bash
pip install -e ./sdk/python
```

```python
from openai import OpenAI
from contextmemory import openai_client_kwargs

client = OpenAI(**openai_client_kwargs(
    base_url="http://localhost:5100/v1",
    api_key="cm_live_...",
    app_id="demo-dev",
    user_id="user-42",
    session_id="sess-abc",
))
```
