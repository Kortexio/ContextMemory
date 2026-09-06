> Part of the ContextMemory docs. [Back to README](../README.md).

# Blueprint 01 — Upstream changelog (CM-1 … CM-7)

Generic runtime enrichments landed in ContextMemory before the CodeMemory fork:

| Phase | Delivered |
|---|---|
| **CM-1** | API v1/v2 routes, `ApiVersionMiddleware`, `SessionState`, `AgentRunState`, `ContextMemoryError`, `AgentTrace`, OpenAPI docs |
| **CM-2** | `IContextBudgetAllocator`, `IContextRetrievalPlanner`, `WorkingMemory`, `MemoryImportance`, compaction preservation |
| **CM-4** | `IAgentStateMachine`, `AgentRetryPolicy`, `IAgentRunStore` / `ResumeAsync`, loop trace |
| **CM-5** | Context / Capability / Execution policies, `SecretClassifier`, enforcement gates |
| **CM-6a** | `ISubagentOrchestrator`, parallel `delegate_task`, max depth / parallel caps |
| **CM-7** | `ILlmModelRouter`, `ILlmModelRegistry`, chat + compaction routing with fallback |

Coding-specific phases (**CM-3**, **CM-6b**, **CM-8**) live in the **CodeMemory** product repository.
