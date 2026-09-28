> Part of the ContextMemory docs. [Back to README](../README.md).

# Show HN — launch kit

Audience: people who self-host LLMs (llama.cpp, vLLM) and want memory without adopting an agent framework or a vector DB. One product only; Cloud is one closing line.

## Title (pick one)

```text
Show HN: ContextMemory – editable markdown memory for your llama.cpp/vLLM server
Show HN: A self-hosted /v1 gateway that gives local LLMs persistent memory
```

Keep "not RAG" out of the title — say it in the body and the first comment, where it can be explained.

## Body

```text
I run models on llama.cpp and vLLM and kept rebuilding the same thing in every app:
history trimming, summaries, "remember this" facts, per-user isolation.

ContextMemory is an open-source gateway that sits in front of any OpenAI-compatible
engine. Your client keeps calling /v1/chat/completions and sends only the new message.
The gateway attaches session memory, calls your engine, and returns a normal
chat.completions response (streaming included).

Memory is markdown on disk (or Postgres): you can open it, edit it, diff it.
No embeddings, no vector DB. When you want more, the same URL can run a server-side
tool loop (wiki search, MCP servers, a code sandbox) with human confirmation before
destructive actions.

Try it in three commands (CPU is enough, llama.cpp pulls a small GGUF):

  git clone https://github.com/Kortexio/ContextMemory && cd ContextMemory
  docker compose -f docker-compose.yml -f docker-compose.llamacpp.yml up --build -d
  ./scripts/aha-chat.sh

The script sends two requests in one session; the second carries only the question,
so the answer can only come from the gateway's memory. CI runs the same check
against a real llama-server on every push.

.NET 9, AGPL-3.0. There is a hosted version (Kortexio Cloud) if you do not want to run it.

https://github.com/Kortexio/ContextMemory
```

## First comment (post immediately after submitting)

```text
Author here. A few things people usually ask:

- Why not RAG / embeddings? Session memory is budgeted markdown the model sees every
  turn. Shared docs go into a "Global Wiki" searched on demand with Postgres FTS
  inside the tool loop. We chose lexical search over markdown you can audit; the
  trade-off is weaker fuzzy recall on paraphrases.
- Why .NET? It is what I ship fastest in and it gives us a single small container.
  You never touch it from your app — it is just an HTTP URL.
- Small models: session memory works with 1.5B instruct models (that is what CI uses).
  The agentic tool loop needs 7B+ with native tool calling; llama.cpp needs --jinja.
- Honest limits: beta, small team, APIs may still move. Engine compatibility
  reports are the most useful issue you can open.
```

## Checklist before posting

- [ ] Latest release published and newer than the last big change (release-please green)
- [ ] `e2e-llamacpp` badge green on `main`
- [ ] No bot issues in the tracker; 3–5 `good first issue` items open
- [ ] GIF or asciinema of `aha-chat.sh` at the top of the README
- [ ] Fresh clone on a clean machine: three commands work as written (Linux + Windows)
- [ ] 4–6 hours free after posting to answer comments

## Timing and follow-up

- Post Tuesday–Thursday, 14:00–16:00 UTC.
- Cross-post to r/LocalLLaMA 24–48 h later with the llama.cpp angle (engine flags, model table).
- Measure for 14 days: unique visitors and referrers (GitHub Insights → Traffic), stars, issues from outside contributors. Clones are inflated by CI and are not a signal.

## Do / don't

| Do | Don't |
|---|---|
| Lead with "your engine + memory you can open" | List other Kortexio products |
| Show the three commands in the body | Promise features that are not on `main` |
| Answer "why not RAG" with the trade-off | Argue that RAG is wrong |
| One line on Cloud at the end | Link pricing |
