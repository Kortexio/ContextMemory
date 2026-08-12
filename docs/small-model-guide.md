> Part of the ContextMemory docs. [Back to README](../README.md).

# Small-model reliability guide

How to run ContextMemory safely with **local / small LLMs** (roughly ≤ 14B parameters: Qwen2.5-3B/7B, Llama-3.2-3B, Gemma-2-9B, Bonsai, etc.).

The reliability story is the same as for large models: **competence ≠ guarantee**. Small models amplify hallucination risk, so the gateway should do more of the work around the model — not trust the model more.

## What the platform already does automatically

[`LlmCapabilitiesResolver`](../src/ContextMemory.Core/Agentic/Prompts/LlmCapabilities.cs) inspects `llmModel` and `promptProfile`:

| Signal | Effect |
|---|---|
| Profile `qwen` / `ollama`, or model name with `≤14b` | `harnessMode = Weak` |
| Weak harness | Aggressive schema sanitize, **inline evidence rules**, prefer client-side tool parsing for Ollama+Qwen |
| Mid-turn compaction | Keeps context under `MaxContextTokens` via `WikiLlmModel` |

You still need a **tenant config** that matches that reality. Auto-harness alone is not enough.

## Recommended preset (opt-in hardening)

```json
{
  "llmBackend": "ollama",
  "llmModel": "qwen2.5:3b",
  "maxContextTokens": 2048,
  "agentic": {
    "enabled": true,
    "promptProfile": "qwen",
    "harnessMode": "weak",
    "tools": {
      "maxMcpToolsPerTurn": 5
    },
    "guardrails": {
      "maxIterations": 6,
      "loopTimeoutSeconds": 45,
      "validationMode": "hybrid",
      "requireZeroExitCode": true,
      "humanReviewOnMaxIterations": true
    }
  }
}
```

Apply via Admin → app → Config, or `PATCH /admin/apps/{appId}/config`.

### Why these knobs

| Knob | Why for small models |
|---|---|
| `harnessMode: weak` | Shorter tool schemas + evidence rules inlined (do not rely on lazy `skill_read`) |
| `maxIterations: 6` | Caps retry loops when guardrails reject invented answers |
| `maxMcpToolsPerTurn: 5` | Small models drown in large `tools[]` catalogs — keep the surface tiny |
| `maxContextTokens: 2048` | Prefer digests + on-demand `wiki_search` over stuffing long history |
| `validationMode: hybrid` | Deterministic guardrails first; LLM-judge only when needed |

## Skills and guardrails to enable

These seeds are **opt-in** (`IsDefaultEnabled: false`). Enable them in Admin → Skills / Guardrails for the app (or platform defaults).

| Id | Type | Role |
|---|---|---|
| `small-model-abstention` | Skill | Prefer "I don't know" + tools over plausible invention |
| `numeric-grounding` | Guardrail | Reject prices / dates / % / elevated counts not present in tool evidence |
| `strict-no-speculation` | Skill | Stronger evidence-only behaviour (also useful on larger models) |
| `source-context-verifier` | Guardrail | Strong IDs (e.g. `PAC-759`) must appear in tool outputs |
| `live-data-evidence-required` | Guardrail | Already default-on when wiki/MCP backends exist |
| `price-quote` | Guardrail | Overlaps monetary cases; keep on for finance tenants |

### Tuning `numeric-grounding`

ConfigJson (Admin or catalog):

```json
{
  "minSpecificsToReject": 1,
  "feedbackEn": "Rejected: numeric values without tool evidence. Emit tool_calls or remove unsupported numbers.",
  "feedbackPt": "Rejeitado: valores numéricos sem evidência de tools. Emite tool_calls ou remove números sem suporte."
}
```

| `minSpecificsToReject` | When |
|---|---|
| `1` | Finance / ops / regulated — zero tolerance |
| `2`–`3` | General chatbots — fewer false positives, slightly less coverage |

**Latency:** happy path is pure CPU (&lt; 0.5 ms). A rejection costs **one extra agent iteration** (another LLM call ± tool) — only when the model was about to invent specifics. Invisible to the end user; they only see the final validated answer (or an honest abstention).

## Suggested stack for small models

```text
User question
  → Static digests + short session wiki (budgeted)
  → Weak harness + small tools[] surface
  → Agentic loop (wiki_search / MCP when needed)
  → Deterministic validators (live-data, source-context, numeric-grounding, …)
  → Optional hybrid LLM-judge
  → Answer or HITL
```

Do **not** expect the small model to self-police. Prefer:

1. **Fewer tools** exposed per turn  
2. **Shorter context** (digests, not full wiki bodies)  
3. **Hard post-checks** that force another tool call or abstention  
4. **HITL** when iterations exhaust (`humanReviewOnMaxIterations`)

## When to escalate to a larger model

Move the tenant (or the turn) to a stronger backend when you need:

- Multi-hop reasoning across many MCP tools in one turn  
- Long unstructured synthesis without schemas  
- Low-latency creative writing where occasional soft errors are acceptable and guardrails would over-reject  

Reliability is a **system** property: model + retrieval + tools + guardrails + process. Small models can be production-safe when the architecture limits what an invented number can do.

## Related docs

- [Inbound MCP guide](inbound-mcp-guide.md) — Azure/Git credentials, MCP vs sandbox fallback  
- [Architecture and features](architecture-and-features.md) — harness modes, discovery loop, seed catalog  
- [HITL](hitl.md) — confirmation and human review  
- [Ops](ops.md) — persistence and troubleshooting  
