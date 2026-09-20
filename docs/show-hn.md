> Part of the ContextMemory docs. [Back to README](../README.md).

# Show HN — suggested blurb

One product only. Link the GitHub repo. Keep Cloud / other Kortexio apps as a single closing line.

## Title

```
Show HN: ContextMemory – OpenAI-compatible gateway with wiki memory (not RAG)
```

## Body

```
Agents forget. Most “memory” products hide facts in vector stores you can’t open.

ContextMemory is an open-source agentic memory gateway: your app keeps calling a
normal OpenAI-compatible /v1/chat/completions URL. The gateway attaches a session
markdown wiki + history, can run a server-side tool loop (sandbox, MCP, wiki search),
and returns a standard chat.completions response.

Bring your own LLM — Ollama, vLLM, LM Studio, ExLlamaSharp, OpenAI, Azure-compatible,
or any /v1 host. Compose defaults to Ollama only for local DX; swap the backend per
tenant in Admin.

Not classic RAG inject. Not a client agent framework. Memory you can open like a wiki.

Aha with the Cursor MCP wedge (two chats):
  A) “Remember: staging DB is postgres-staging-01” → memory_save
  B) new chat “What is our staging DB?” → memory_search

Repo: https://github.com/Kortexio/ContextMemory
Self-host / Cloud: see README. GIF storyboard: docs/aha-demo.html
```

## Do / don’t

| Do | Don’t |
|---|---|
| Lead with wiki memory + `/v1` drop-in | List CompanyBrain / TriageHub / Fincheck in the title |
| Say BYO LLM explicitly | Imply the product is “an Ollama app” |
| Point to Compose + Admin LLM picker | Promise a public live chat sandbox unless it exists |
| One soft line on Kortexio Cloud at the end | Dump six repos into the first paragraph |
