# First adversarial A/B — suite

Canonical numbers and the weak-harness lift list live in [README.md](README.md).

| | |
|---|---|
| Run | `qwen35b-suite-2026-11-15T20-47-56-653Z` |
| Model | `qwen3.6-35b-iq3s-mtp` @ OpenAI-compatible `/v1` (`CM_BENCH_BASE_URL`) |
| **Mean** | **off=13.5 · on=100 · Δ=+86.5** |

```powershell
$env:CM_BENCH_BASE_URL = "http://127.0.0.1:8080"   # your OpenAI-compatible /v1 host
$env:CM_BENCH_MODEL    = "qwen3.6-35b-iq3s-mtp"
node benchmarks/bench.mjs selftest
node benchmarks/bench.mjs ab --label qwen35b-suite
node benchmarks/bench.mjs report
```
