"""Thin ContextMemory client: OpenAI-compatible base URL + required headers."""

from __future__ import annotations

from typing import Any, Mapping


def headers(
    *,
    api_key: str,
    app_id: str | None = None,
    user_id: str | None = None,
    session_id: str | None = None,
    extra: Mapping[str, str] | None = None,
) -> dict[str, str]:
    """Build auth/tenant headers for ContextMemory (self-host or Cloud)."""
    h: dict[str, str] = {
        "Authorization": f"Bearer {api_key}",
        "Content-Type": "application/json",
    }
    if app_id:
        h["X-App-Id"] = app_id
    if user_id:
        h["X-User-Id"] = user_id
    if session_id:
        h["X-Session-Id"] = session_id
    if extra:
        h.update(extra)
    return h


def openai_client_kwargs(
    *,
    base_url: str,
    api_key: str,
    app_id: str | None = None,
    user_id: str | None = None,
    session_id: str | None = None,
) -> dict[str, Any]:
    """Keyword args for openai.OpenAI(...) or AsyncOpenAI(...)."""
    return {
        "base_url": base_url.rstrip("/"),
        "api_key": api_key,
        "default_headers": headers(
            api_key=api_key,
            app_id=app_id,
            user_id=user_id,
            session_id=session_id,
        ),
    }


__all__ = ["headers", "openai_client_kwargs"]
