# First adversarial A/B — suite

Canonical numbers live in [README.md](README.md) **Measured**.

| | |
|---|---|
| Run | `qwen35b-suite-2026-11-15T16-48-55-844Z` |
| Model | `qwen3.6-35b-iq3s-mtp` @ `llm.example.test` |
| **Mean** | **off=20 · on=96 · Δ=+76** |

```powershell
$env:CM_BENCH_BASE_URL = "https://llm.example.test"
$env:CM_BENCH_MODEL    = "qwen3.6-35b-iq3s-mtp"
node benchmarks/bench.mjs selftest
node benchmarks/bench.mjs ab --label qwen35b-suite
node benchmarks/bench.mjs report
```
