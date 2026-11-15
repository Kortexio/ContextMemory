# ContextMemory Agentic Benchmarks

Adversarial harness proof — **not** a vanity scoreboard.  
Rigor follows CodeForge honesty (baseline must hurt; hidden checks; no soft suite); domain stays wiki / MCP / finish / stuck.

## Measured (live)

| | |
|---|---|
| Run id | `qwen35b-suite-2026-11-15T16-48-55-844Z` |
| Host | `https://llm.example.test` · model `qwen3.6-35b-iq3s-mtp` |
| Protocol | **Off** = Qwen client-side wire, no family/prose/finish/Continue · **On** = weak harness |
| Auth | none |

**Mean: off=20 · on=96 · Δ=+76**

| Task | Off | On | Δ | Off falseDone | On falseDone | Notes (this run) |
|---|---:|---:|---:|:---:|:---:|---|
| CM1-wiki-live-data | 10 | 100 | +90 | yes | no | Off: “I do not know” sem tools; On: `wiki_search` → `postgres-staging-01` |
| CM2-mcp-tool-args | 20 | 90 | +70 | yes | no | Off: JSON de tool sem lift; On: `bench-crm__query_objects` → Acme Staging Holdings (HITL max-iter, `pass=false`) |
| CM3-finish-evidence | 25 | 90 | +65 | yes | no | Off: max-iter sem tools; On: buscou evidência, parcial “No” + HITL (`pass=false`) |
| CM4-anti-stuck | 35 | 100 | +65 | yes | no | Sticky wiki: Off inventou host; On noise → pivot → `postgres-staging-01` |
| CM5-distractor | 10 | 100 | +90 | yes | no | Off inventou host; On: host certo |

Artifacts: `results/qwen35b-suite-2026-11-15T16-48-55-844Z.json` · `node benchmarks/bench.mjs report`

Honesty: off hurts on every seed; on recovers. CM2/CM3 on scored high but `pass=false` (iteration/HITL ceiling after evidence) — not vanity green.

**Follow-up (phantom MCP fix):** after remapping `server__*` / blocking invented MCP names + cleaning nudges, probe `cm2-phantom-fix` → **CM2 on=100** in 2 iters (`bench-crm__query_objects` direto). Suite mean above still the full `qwen35b-suite` run.

---

## A/B arms

| Arm | Wire | Recovery |
|---|---|---|
| **Off** | Same hard Qwen client-side catalog | No family lift, no prose promote, no finish gate, no Continue |
| **On** | Weak harness | Full family / repair / finish / Continue |

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
| `CM_BENCH_BASE_URL` | `https://llm.example.test` | OpenAI-compatible `/v1` host |
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
$env:CM_BENCH_BASE_URL = "https://llm.example.test"
$env:CM_BENCH_MODEL    = "qwen3.6-35b-iq3s-mtp"
node benchmarks/bench.mjs ab --label qwen35b-suite
```

Live A/B uses `ContextMemory.Bench`. Pass `--stub-only` for protocol stub only.

Score weights (default / CF-ish): **evidence ~60 · tool · protocol · honesty** → 0–100 (per-task overrides in `task.json`).
