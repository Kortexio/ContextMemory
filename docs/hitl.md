> Part of the ContextMemory docs. [Back to README](../README.md).

## Human-in-the-loop — how it works

1. The model proposes a tool that matches `requireConfirmationFor` (e.g. a command containing `delete`).
2. Execution **stops**; the API returns a confirmation prompt and header `X-Context-Memory-Agentic-Awaiting-Confirmation`.
3. The user replies with confirmation (e.g. `confirm`, `approve`, or `[CONFIRM:abc123]`).
4. The tool runs; the loop continues until a validated final answer.
5. If the iteration limit is reached, `humanReviewOnMaxIterations` requests **approval of the partial answer**.

Everything is recorded in the session `log.md` for audit.

---

