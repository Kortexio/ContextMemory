# Engine compatibility (minimal)

ContextMemory is engine-agnostic: any OpenAI-compatible `/v1` server works. This table is the **minimum** we stand behind for OSS single-node.

| Workload | Guidance |
|---|---|
| **Memory-only** (inject wiki / session context, no tool loop) | Small instruct models work in CI (~1.5B). Prefer any instruct chat model your engine serves. |
| **Agentic** (tools, evidence finish, Weak harness) | Prefer **≥ ~7B** instruct. Enable the **Weak** harness for small/local models. See [small-model-guide.md](small-model-guide.md). |

| Engine family | Memory | Agentic |
|---|---|---|
| llama.cpp (`--jinja`) | Yes (CI uses ~1.5B) | Prefer ≥~7B instruct + Weak harness |
| Ollama | Yes | Prefer ≥~7B instruct + Weak harness |
| vLLM / LM Studio / ExLlamaSharp / OpenAI-compatible cloud | Yes | Prefer ≥~7B (or frontier) + harness as needed |

Live A/B numbers: [`benchmarks/`](../benchmarks/). Ops notes: [ops.md](ops.md#engines).

Found a combo that works or fails? Open a GitHub **engine compatibility** issue with model id + flags.
