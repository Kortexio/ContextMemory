using System.Text.Json;
using ContextMemory.Core.Agentic;

namespace ContextMemory.Core.Agentic;

/// <summary>English system seed for skills and guardrail packs.</summary>
public static class AgenticCatalogSeed
{
    public static IReadOnlyList<AgenticSkillDefinition> Skills { get; } = BuildSkills();

    public static IReadOnlyList<AgenticGuardrailDefinition> Guardrails { get; } = BuildGuardrails();

    private static IReadOnlyList<AgenticSkillDefinition> BuildSkills()
    {
        var now = DateTimeOffset.UnixEpoch;
        return
        [
            Skill("anti-hallucination-web", "Anti-hallucination (web)", "safety", 10, true,
                "Never invent website/product/API facts; fetch URLs before describing them.",
                """
                ## Anti-hallucination (web)
                - Never invent facts about websites, products, APIs, or companies.
                - If the user asks about a URL/site/page, you MUST fetch it with tools first
                  (`python_execute` + httpx/BeautifulSoup or Playwright, or web-search tools).
                - After tools return, answer ONLY from tool output. If the fetch fails, say so.
                - Do not invent comparisons unless the user asked and you have evidence.
                - Prefer tool_calls over a confident wrong answer.
                - Reply in the user's language.
                """,
                ["url-fetch-required"]),

            Skill("sandbox-facts-selfhosted", "Self-hosted sandbox facts", "sandbox", 20, true,
                "Correct capabilities of the self-hosted sandbox (not ACA).",
                """
                ## Self-hosted sandbox facts
                - `python_execute` / `shell_execute` / `node_execute` run on the **self-hosted sandbox**, NOT Azure Container Apps.
                - Outbound HTTP(S) **works**. Do not claim DNS/network isolation or ACA sandbox restrictions.
                - Files are ephemeral (deleted after each call); print results to stdout.
                - Reply in the user's language.
                """),

            Skill("prefer-mcp-over-adhoc", "Prefer MCP over ad-hoc HTTP", "integrations", 30, true,
                "Use configured MCP servers instead of hand-rolled OAuth/HTTP.",
                """
                ## Prefer MCP over ad-hoc HTTP
                - When an MCP integration is configured (e.g. Zuora), you MUST use MCP tools (`server__tool`, especially `…__zuora_graphql`).
                - Do NOT call Zuora (or other MCP-backed APIs) via `python_execute` / `requests` / hand-rolled OAuth or `/api/v1/*`.
                - Do NOT ask the user for client id, client secret, or access tokens when MCP credentials are already configured.
                - If MCP fails, report the MCP error. Do not fall back to inventing REST scripts or placeholder credentials.
                - Reply in the user's language.
                """),

            Skill("tool-calling-discipline", "Tool-calling discipline", "tools", 40, true,
                "When and how to call tools safely and efficiently.",
                """
                ## Tool-calling discipline
                - Invoke tools only when needed: external action, live data/pages, or app documentation via `wiki_search`.
                - When you need a tool: emit **only** `tool_calls` (or the backend's function-calling form) with valid JSON — no extra narration.
                - After receiving tool results, synthesize the final answer in natural language.
                - MCP tools use the `server__tool` format (e.g. `crm__get_customer`).
                - If a tool fails (exit code ≠ 0), explain the error clearly.
                - Never perform destructive actions without explicit user confirmation.
                - Reply in the user's language.
                """),

            Skill("ground-answers-in-evidence", "Ground answers in evidence", "safety", 50, true,
                "Stay faithful to tools, wiki, and session evidence.",
                """
                ## Ground answers in evidence
                - Do not invent numbers, IDs, quotes, or API fields.
                - If evidence is missing, say so and fetch with tools when possible.
                - Reply in the user's language.
                """),

            Skill("wiki-first-for-docs", "Wiki-first for internal docs", "tools", 60, true,
                "Prefer ingested knowledge via wiki_search.",
                """
                ## Wiki-first for internal docs
                - Use `wiki_search` for Jira/Confluence/SQL/docs already ingested into the app before guessing.
                - Prefer wiki evidence over memory when they conflict.
                - Reply in the user's language.
                """),

            Skill("clarify-when-ambiguous", "Clarify when ambiguous", "behavior", 70, true,
                "Ask when the goal is underspecified.",
                """
                ## Clarify when ambiguous
                - Ask one short clarifying question when the goal is underspecified.
                - Do not invent requirements or assumptions presented as facts.
                - Reply in the user's language.
                """),

            Skill("privacy-and-secrets", "Privacy and secrets", "safety", 80, true,
                "Protect credentials and secrets.",
                """
                ## Privacy and secrets
                - Never invent or echo API keys, passwords, tokens, or full secrets.
                - If a tool accidentally returns a secret, redact it in the user-facing answer.
                - Reply in the user's language.
                """),

            Skill("transparent-failures", "Transparent failures", "behavior", 90, true,
                "Be honest about tool errors.",
                """
                ## Transparent failures
                - If a tool fails, report the error clearly and suggest a next step.
                - Never pretend a failed tool succeeded.
                - Reply in the user's language.
                """),

            Skill("concise-professional", "Concise professional tone", "behavior", 100, true,
                "Clear, direct answers without filler.",
                """
                ## Concise professional tone
                - Lead with the answer.
                - Avoid filler, fake enthusiasm, and unnecessary tables or comparisons.
                - Reply in the user's language.
                """),

            Skill("strict-no-speculation", "Strict no speculation", "safety", 200, false,
                "Refuse any claim not backed by tool/wiki/session evidence.",
                """
                ## Strict no speculation
                - Refuse any claim not backed by tool, wiki, or session evidence.
                - Prefer "I don't have evidence yet" + tool_calls over speculation.
                - Reply in the user's language.
                """),

            Skill("step-by-step-reasoning-brief", "Brief plan before tools", "behavior", 210, false,
                "One short plan line before tool_calls on complex tasks.",
                """
                ## Brief plan before tools
                - On complex multi-step tasks, state one short plan line, then emit tool_calls.
                - Do not replace tools with long chain-of-thought narration.
                - Reply in the user's language.
                """),

            Skill("rule-always-evidence", "Always-on: evidence first", "rules", 5, true,
                "Always-on rule: never invent unsupported facts.",
                """
                ## Always-on: evidence first
                - Prefer tools, wiki_search/wiki_grep, and session artifacts over speculation.
                - Tag uncertain claims clearly.
                """,
                activation: AgenticSkillActivation.AlwaysOn),

            Skill("rule-requestable-style", "Requestable: terse style", "rules", 230, true,
                "Optional rule for ultra-terse answers.",
                """
                ## Terse style
                - Answer in at most 5 short bullets unless the user asks for detail.
                - No preamble.
                """,
                activation: AgenticSkillActivation.Requestable),

            Skill("zuora-graphql-discover-first", "Zuora GraphQL — discover first", "integrations", 220, false,
                "Discover Zuora GraphQL schema with tools before any table/query.",
                """
                ## Zuora GraphQL — discover before query
                You do NOT know this tenant's Zuora GraphQL schema from memory. Always discover with tools first.

                ### ID heuristics
                - Values like `A-S########` are usually **Subscription Number**, NOT Account Id.
                - Account numbers and Zuora object Ids are different fields — do not guess which filter to use.

                ### Mandatory discovery sequence (before any data fetch)
                1. Prefer MCP tool `…__zuora_graphql` (never invent OAuth/HTTP).
                2. If unsure of the object: `operation=list_types`.
                3. For the chosen object: `operation=filter_keys` with the correct `entryPoint`.
                4. If needed: `operation=describe_types` / `describe_input` for fields and filter inputs.
                5. Only then: `table` / `build` / `query` using **only** entry points, filter keys, and fields returned by discovery.

                ### Hard rules
                - Entry points are usually **plural** (e.g. `accounts`, `subscriptions`) — never invent singular root fields like `account` / `subscription` unless discovery returned them.
                - `table` always requires `fields` taken from describe/filter discovery.
                - On GraphQL `FieldUndefined` / `WrongType`: do **not** retry the same query. Re-run discovery and change entryPoint/field/filter.
                - After 2 consecutive validation errors on the same object, stop guessing: summarize what discovery returned and ask the user which object/id they mean.
                - Empty `count=0` after a valid query is a real answer — report "not found", do not keep inventing alternate queries forever.

                ### Reply
                Answer from tool output only. Reply in the user's language.
                """)
        ];

        AgenticSkillDefinition Skill(
            string id,
            string name,
            string category,
            int sort,
            bool defaultEnabled,
            string description,
            string markdown,
            string[]? linked = null,
            string activation = AgenticSkillActivation.Skill) =>
            new()
            {
                Id = id,
                Name = name,
                Category = category,
                Activation = activation,
                SortOrder = sort,
                IsSystem = true,
                IsDefaultEnabled = defaultEnabled,
                Description = description,
                PromptMarkdown = markdown.Trim(),
                LinkedGuardrailIds = linked ?? [],
                CreatedAt = now,
                UpdatedAt = now
            };
    }

    private static IReadOnlyList<AgenticGuardrailDefinition> BuildGuardrails()
    {
        var now = DateTimeOffset.UnixEpoch;
        return
        [
            new AgenticGuardrailDefinition
            {
                Id = "url-fetch-required",
                Name = "URL fetch required",
                Description = "Reject answers that describe a URL/site without tool fetch evidence.",
                Kind = AgenticGuardrailKinds.UrlFetch,
                ConfigJson = JsonSerializer.Serialize(new
                {
                    kind = AgenticGuardrailKinds.UrlFetch,
                    feedbackEn =
                        "Rejected: you described a website/URL without fetching it. Emit tool_calls first (python_execute + httpx/Playwright or web-search), then answer ONLY from tool output.",
                    feedbackPt =
                        "Rejeitado: descreveste um site/URL sem o ires buscar. Emite tool_calls primeiro (python_execute + httpx/Playwright ou web-search) e responde APENAS com base no output."
                }),
                IsSystem = true,
                IsDefaultEnabled = true,
                SortOrder = 10,
                UpdatedAt = now
            },
            new AgenticGuardrailDefinition
            {
                Id = "sandbox-claim-reject",
                Name = "Reject fabricated sandbox limits",
                Description = "Reject false ACA/no-network claims when self-hosted sandbox is configured.",
                Kind = AgenticGuardrailKinds.SandboxClaim,
                ConfigJson = JsonSerializer.Serialize(new
                {
                    kind = AgenticGuardrailKinds.SandboxClaim,
                    feedbackEn =
                        "Rejected: you invented false sandbox limitations. This tenant uses self-hosted-sandbox with outbound HTTP. Emit tool_calls — do not narrate hypothetical failures.",
                    feedbackPt =
                        "Rejeitado: inventaste limitações falsas do sandbox. Este tenant usa self-hosted-sandbox com HTTP externo. Emite tool_calls — não narres falhas hipotéticas."
                }),
                IsSystem = true,
                IsDefaultEnabled = true,
                SortOrder = 20,
                UpdatedAt = now
            },
            new AgenticGuardrailDefinition
            {
                Id = "live-data-evidence-required",
                Name = "Live data evidence required",
                Description = "Reject live-data answers (accounts, invoices, …) without successful MCP/wiki tool evidence.",
                Kind = AgenticGuardrailKinds.LiveDataEvidence,
                ConfigJson = JsonSerializer.Serialize(new
                {
                    kind = AgenticGuardrailKinds.LiveDataEvidence,
                    feedbackEn =
                        "Rejected: live-data question without successful MCP/wiki evidence. Emit tool_calls (e.g. query_objects); do not invent IDs or statuses.",
                    feedbackPt =
                        "Rejeitado: pergunta de dados live sem evidência MCP/wiki bem-sucedida. Emite tool_calls (ex. query_objects); não inventes IDs nem estados."
                }),
                IsSystem = true,
                IsDefaultEnabled = true,
                SortOrder = 25,
                UpdatedAt = now
            },
            new AgenticGuardrailDefinition
            {
                Id = "require-error-disclosure",
                Name = "Require error disclosure",
                Description = "Reject final answers that ignore failed tool steps.",
                Kind = AgenticGuardrailKinds.ToolFailureDisclosure,
                ConfigJson = JsonSerializer.Serialize(new { kind = AgenticGuardrailKinds.ToolFailureDisclosure }),
                IsSystem = true,
                IsDefaultEnabled = true,
                SortOrder = 30,
                UpdatedAt = now
            },
            new AgenticGuardrailDefinition
            {
                Id = "block-credential-leak",
                Name = "Block credential leak patterns",
                Description = "Reject answers matching secret-like substrings.",
                Kind = AgenticGuardrailKinds.BlockedPatterns,
                ConfigJson = JsonSerializer.Serialize(new
                {
                    kind = AgenticGuardrailKinds.BlockedPatterns,
                    patterns = new[] { "api_key=", "Bearer ey", "client_secret", "password=" }
                }),
                IsSystem = true,
                IsDefaultEnabled = true,
                SortOrder = 40,
                UpdatedAt = now
            },
            new AgenticGuardrailDefinition
            {
                Id = "pre-tool-deny-rm-rf",
                Name = "PreToolUse deny destructive shell patterns",
                Description = "Deny shell_execute when arguments look like recursive force delete.",
                Kind = AgenticGuardrailKinds.PreToolUse,
                ConfigJson = JsonSerializer.Serialize(new
                {
                    matchToolPatterns = new[] { "shell_execute" },
                    denyToolPatterns = Array.Empty<string>(),
                    requireConfirm = false
                }),
                IsSystem = true,
                IsDefaultEnabled = false,
                SortOrder = 50,
                UpdatedAt = now
            },
            new AgenticGuardrailDefinition
            {
                Id = "post-tool-redact-secrets",
                Name = "PostToolUse redact secrets",
                Description = "Redact common secret patterns from tool observations.",
                Kind = AgenticGuardrailKinds.PostToolUse,
                ConfigJson = JsonSerializer.Serialize(new
                {
                    matchToolPatterns = new[] { "*" },
                    redactPatterns = new[] { @"(?i)(api[_-]?key|token|secret)\s*[:=]\s*\S+" },
                    maxOutputChars = 0
                }),
                IsSystem = true,
                IsDefaultEnabled = true,
                SortOrder = 60,
                UpdatedAt = now
            }
        ];
    }
}
