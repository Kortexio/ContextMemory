# ContextMemory Agentic Benchmarks

Adversarial harness proof — **not** a vanity scoreboard.  
Rigor follows CodeForge honesty (baseline must hurt; hidden checks; no soft suite); domain stays wiki / MCP / finish / stuck.

## Measured (live)

| | |
|---|---|
| Run id | `qwen35b-suite-2026-11-15T20-47-56-653Z` |
| Host | OpenAI-compatible `/v1` (`CM_BENCH_BASE_URL`) · model `qwen3.6-35b-iq3s-mtp` |
| Protocol | **Off** = Qwen client-side wire, no family/prose/finish/Continue · **On** = weak harness |
| Auth | none |

**Mean: off=13.5 · on=100 · Δ=+86.5**

| Task | Off | On | Δ | Off falseDone | On falseDone | Notes (this run) |
|---|---:|---:|---:|:---:|:---:|---|
| CM1-wiki-live-data | 10 | 100 | +90 | yes | no | Off: “I do not know”; On: `wiki_search` → `postgres-staging-01` |
| CM2-mcp-tool-args | 10 | 100 | +90 | yes | no | Off: max-iter HITL; On: `bench-crm__query_objects` → Acme Staging Holdings |
| CM3-finish-evidence | 25 | 100 | +75 | yes | no | Off: max-iter; On: MCP + wiki → “No” with evidence |
| CM4-anti-stuck | 17.5 | 100 | +82.5 | yes | no | Sticky wiki: On noise → pivot → `postgres-staging-01` |
| CM5-distractor | 5 | 100 | +95 | yes | no | On: authoritative host, not distractor |

Artifacts: `results/qwen35b-suite-2026-11-15T20-47-56-653Z.json` · `node benchmarks/bench.mjs report`

Honesty: off hurts on every seed; on recovers 5/5 with tools. Prior run `…T16-48-55…` was off=20 / on=96 (CM2/CM3 HITL after evidence).

---

## Weak harness — what makes a weak model run stronger

`ModelHarnessMode.Weak` (auto for local Qwen-class / explicit `harnessMode`) is the recovery stack measured by **On**. Same prompts and model as **Off**; only the lifts below are enabled.

### Wire & catalog

1. **Client-side tool catalog** — for Qwen/Weak, omit native `tools[]` (avoids Ollama XML/template 500s); inject a compact catalog into the system prompt and flatten history for the wire.
2. **Native → client-side fallback** — if native tool-call parse fails, retry with the client-side catalog path.
3. **Schema compact** — MCP/tool JSON schemas keep types + required; long property descriptions are truncated so weak context windows fit.
4. **Aggressive schema sanitize** — strip grammar-hostile bits before send on Weak.
5. **Graduated schema repair** — on LLM grammar reject, retry with progressively simpler schemas (refund iteration).
6. **MCP top-K** — Weak clamps offered MCP tools (hint 6, absolute ≤12) via `McpToolSelector` (relevance + recent tools), not the full catalog dump.
7. **Tool system contract** — capability-driven prompt section (discovery / sandbox / HTTP / browser finish rules) from active tools’ `ToolCapabilities`, not hardcoded name lists.

### Tool-call recovery (protocol lifts)

8. **Family recovery** — parse model-family markup (Qwen, DeepSeek-R1, DSML, GLM, Harmony, Mistral, Llama, Kimi, Gemma4, Granite, MiniMax, OLMo, Phi4, …) into structured `tool_calls`.
9. **Prose → tool_calls promotion** — when the model writes a catalog call in chat text, promote it to executable calls (capped / validated per turn).
10. **Phantom tool-use retry** — model announces a catalog tool in prose but emits no call → nudge and retry (cap 2).
11. **Argument repair** — coerce JSON to schema (types, drop nulls/empty placeholders, unwrap stringified objects); recover mangled tool names.
12. **MCP phantom name remap** — invented / wrong `server__tool` names remapped onto the active qualified catalog (`McpPhantomToolResolver`).
13. **Pythonic / XML family args** — family parsers accept non-JSON argument shapes where those models emit them.

### Loop budget & anti-stuck

14. **Iteration refunds** — recoverable rejects (schema/overflow/repair) refund a step (Weak: up to 12) under an absolute ceiling (40).
15. **Continue extensions** — at max iterations, extend with a “Continue: prefer tools or finish with evidence” nudge (Weak: up to 8) before HITL.
16. **Loop / stall detector** — fingerprint identical tool+args+result streaks; break when stuck (Weak stall threshold 3).
17. **Duplicate tool-call guard** — reject identical successful/failed repeats; wiki query budget; empty-query reject.
18. **Force-answer circuit breakers** — after wiki-budget / duplicate-after-success / repeated failure, strip tools and force a final answer (with evidence fallback when the model only echoes harness text).
19. **Context overflow shrink** — on `n_ctx` exceeded, shrink prompt budget and retry (refund).

### Finish quality

20. **Evidence finish gate** — refuse discovery-only or live-data-without-evidence finishes; nudge up to `MaxFinishNudges` (Weak: 3).
21. **Inline evidence rules** — Weak system prompt embeds short evidence / finish rules so the model sees them without relying on native tool discipline.
22. **Deterministic validator + guardrails** — empty answer, destructive confirm, source/numerics/price grounding, sandbox claims, URL fetch, thinking-leak, tool-intent narration, etc. (policy pack; not Off-disabled).
23. **Capability-stamped steps** — gates read `ToolCapabilities` on each step (Discovery / Sandbox / QueryFocused / …), so new tools plug in via DI descriptors without harness name lists.

### What Off deliberately disables

Same hard Qwen client-side wire, but **no** family lift, prose promote, arg repair, finish gate, Continue/refunds, phantom retry, or aggressive sanitize — so the baseline must hurt if the suite is honest.

| Arm | Wire | Recovery |
|---|---|---|
| **Off** | Hard Qwen client-side catalog | None of the lifts above |
| **On** | Weak harness | Full stack |

`CM_BENCH_API_KEY` is **optional** — only if the API under test requires a key.

## Honesty principles

1. **Baseline must hurt** — if harness-off already passes easily, the task is invalid.
2. **`baselineProbe`** — every seed includes a probe the selftest must **fail** (CodeForge-style).
3. **Hidden checks** — facts/tools live outside the model prompt.
4. **No intent crutches** — scorer looks at tool steps + final answer + fixtures only.
5. **Hard negatives** — wiki/MCP distractors and empty MCP args.
6. **Honesty > mean score** — report off and on, plus `stuck` / `badArgs` / `falseDone`.

Failure classes: `A-harness` | `B-protocol` | `C-semantic`.

## Suite

| ID | Domain | Why off should fail |
|---|---|---|
| **CM1** | Wiki live-data | Client-side markup without lift → no tools / falseDone |
| **CM2** | MCP `query_objects` | Empty/`{}` args → badArgs; mock CRM returns Acme Staging Holdings |
| **CM3** | Finish evidence | Prompt tempts unsourced Yes/No |
| **CM4** | Anti-stuck | Sticky wiki: 1st query noise → must pivot; identical repeats → `stuck` |
| **CM5** | Distractor | Wrong wiki host as chosen answer |

## Environment

| Variable | Default | Role |
|---|---|---|
| `CM_BENCH_BASE_URL` | _(required for live A/B)_ | OpenAI-compatible `/v1` host under test |
| `CM_BENCH_MODEL` | `qwen3.6-35b-iq3s-mtp` | Model id |
| `CM_BENCH_API_KEY` | _(none)_ | Optional target API key |

## How to run

```bash
node benchmarks/bench.mjs list
node benchmarks/bench.mjs selftest
node benchmarks/bench.mjs ab --label qwen35b-suite
node benchmarks/bench.mjs report
```

```powershell
$env:CM_BENCH_BASE_URL = "http://127.0.0.1:8080"   # your OpenAI-compatible /v1 host
$env:CM_BENCH_MODEL    = "qwen3.6-35b-iq3s-mtp"
node benchmarks/bench.mjs ab --label qwen35b-suite
```

Live A/B uses `ContextMemory.Bench`. Pass `--stub-only` for protocol stub only.

Score weights (default / CF-ish): **evidence ~60 · tool · protocol · honesty** → 0–100 (per-task overrides in `task.json`).
