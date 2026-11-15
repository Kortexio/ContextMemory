# Changelog

## 0.2.0-beta (2026-11-15)


### Features

* add Cursor-style HTTP, vision, browser, PDF, and canvas agent tools. ([1c60610](https://github.com/Kortexio/ContextMemory/commit/1c60610d70b5917c378dd368af4b5420ae9cdb59))
* **agentic:** raise wiki call budget to 5 distinct queries ([7cfac22](https://github.com/Kortexio/ContextMemory/commit/7cfac22214c428caaf6bd748a9372e6eb194e7ac))
* bundle llama.cpp and vLLM compose overrides with an end-to-end memory check ([7bf85f5](https://github.com/Kortexio/ContextMemory/commit/7bf85f52ff4782af0dd7090a5a6eead82654d61a))
* derive LLM protocol capabilities from prompt profiles. ([99269b9](https://github.com/Kortexio/ContextMemory/commit/99269b9d710f8d3dd8a3ec88a668829f4dca356d))
* expand agentic guardrail catalog with enable/disable kinds ([7a20808](https://github.com/Kortexio/ContextMemory/commit/7a2080865d1af35df76835f53e5f00ff8bdbbcc1))
* expose LLM generation defaults in Admin and apply to chat/generate payloads ([6b9ee5e](https://github.com/Kortexio/ContextMemory/commit/6b9ee5e6a9cc65c406cb10c6cf8c2f3a94224b5f))
* mark public beta as v0.0.1-beta ([4965e7a](https://github.com/Kortexio/ContextMemory/commit/4965e7a387320dfd97a2c2df2a4ac83e200e7fee))
* multi-model harness, live-data guardrail, and Ollama num_ctx fix. ([97da9ed](https://github.com/Kortexio/ContextMemory/commit/97da9ed88fe516cfea09ab934df1f2d226a237bc))
* pluggable tool descriptors by capability, not wire-name lists ([3fc14fb](https://github.com/Kortexio/ContextMemory/commit/3fc14fb39fe5d14e440ade55d784e400cc75b264))
* self-host launch readiness (llama.cpp / vLLM, e2e memory check, cleaner public signals) ([6118592](https://github.com/Kortexio/ContextMemory/commit/6118592bddf0c7ac137b1840e5a2051fd857cbef))


### Bug Fixes

* **agentic:** distinguish wiki budget controls from failures ([36c1db9](https://github.com/Kortexio/ContextMemory/commit/36c1db96e83009bfa1572a2260c16561e7263bda))
* **agentic:** reject repeated response sections ([ae54105](https://github.com/Kortexio/ContextMemory/commit/ae54105643a3e28f073011e9b126e57a5e46d6ed))
* **ci:** stop GITHUB_TOKEN from overriding PEER_SYNC_TOKEN on mirror push ([b2a4766](https://github.com/Kortexio/ContextMemory/commit/b2a47664e76641dd64ed73cae22b0cb37a243146))
* coerce query_objects filter object/string into string array. ([63a74ca](https://github.com/Kortexio/ContextMemory/commit/63a74ca0d36ca30b3a55c9462c09c0e24fbd2e69))
* map fieldsToReturn alias in query_objects normalizer. ([4443ae2](https://github.com/Kortexio/ContextMemory/commit/4443ae2fea823685569d7e2eabf2fbfb5fdb1e9e))
* **mcp:** NDJSON framing for azure-monitor-mcp ([e1b0b4b](https://github.com/Kortexio/ContextMemory/commit/e1b0b4b8c3e62f2c27a84ef7a6cf61ab43f8a3c2))
* **mcp:** speak NDJSON in azure-monitor-mcp for mcp-runtime ([587762e](https://github.com/Kortexio/ContextMemory/commit/587762e57ef669add3cc0648035d0041de2e39ff))
* **migrations:** cast ConfigJson updates to jsonb. ([1a2c19c](https://github.com/Kortexio/ContextMemory/commit/1a2c19cdb3c04107765d4dc81697bdaa56df059b))
* pin LinkedIn API version and clarify dev.to errors ([94d5fbd](https://github.com/Kortexio/ContextMemory/commit/94d5fbd010f1a92a0a92baaec0385253abbc59b2))
* prevent ArgumentOutOfRange on agentic string truncation ([ea73ba7](https://github.com/Kortexio/ContextMemory/commit/ea73ba7f6d35ff424f54052cce9226e600bec0db))
* prevent ArgumentOutOfRange on agentic string truncation. ([8386da0](https://github.com/Kortexio/ContextMemory/commit/8386da0f4c7950968102a54f9b50f6fa3b975b95))
* promote prose MCP tool JSON into structured tool_calls. ([f0621e1](https://github.com/Kortexio/ContextMemory/commit/f0621e16172b7f7fbf4e1b8f9976d69e7c79d12c))
* publish thin helpers under available Kortexio package names ([ec5aa4a](https://github.com/Kortexio/ContextMemory/commit/ec5aa4afb342121529538f5d186facd2bc5d5fce))
* regenerate AddSkillActivation migration with model snapshot. ([40b4f52](https://github.com/Kortexio/ContextMemory/commit/40b4f5279b44d6e02482f4520f372f76f8b02f0e))
* rewrite SQL-style query_objects filters to Zuora field.OP:value. ([2653e11](https://github.com/Kortexio/ContextMemory/commit/2653e11a97b8825952a706b6c4609893301336d3))
* set User-Agent on dev.to announcer requests ([50bf598](https://github.com/Kortexio/ContextMemory/commit/50bf59815c8874b3076b28fa9c1d404d08afff68))
* stabilize Qwen client-side tools and stop capping Weak MCP catalogs. ([49ef48f](https://github.com/Kortexio/ContextMemory/commit/49ef48f1adbad4f2858752758d04db715c65a40f))
* stop live-data validation loops and soften noisy guardrail defaults ([b51739a](https://github.com/Kortexio/ContextMemory/commit/b51739a4816e760cea5d0c1e0f73c96682776c58))
* stop RequireZeroExitCode guardrail from looping forever on retried tool failures ([d2fd9b0](https://github.com/Kortexio/ContextMemory/commit/d2fd9b06752555bda544ef57a1baf4849b2c7d6d))
* stop tool_describe failures from poisoning agentic validation loops. ([72ed5b3](https://github.com/Kortexio/ContextMemory/commit/72ed5b3bd724f3146e35931535aea3106dde3c4f))
* tighten LinkedIn release posts to messaging voice ([cde7e4c](https://github.com/Kortexio/ContextMemory/commit/cde7e4c4ab28eb840e845e55c572763120c05e62))

## [0.3.1-beta](https://github.com/Kortexio/ContextMemory/compare/v0.2.0-beta...v0.3.1-beta) (2026-11-15)

### Features

* **agentic:** pluggable tools via `IToolDescriptor` + `ToolCapabilities` (no wire-name lists in the loop)
* **harness:** measured weak-harness A/B lift on adversarial suite (off → on); README proof section
* **helpers:** rename `sdk/` → `helpers/`; packages documented as header helpers (not a client SDK)
* **docs:** API freeze notes (`/v1` agentic/HITL metadata + Admin HTTP); ops single-node checklist; engines matrix
* **ci:** `bench-selftest` workflow; aha-chat supports `CONTEXTMEMORY_STREAM=1`

### Bug Fixes

* scrub internal LLM host from docs/defaults; bench artifacts redact `CM_BENCH_BASE_URL`

## [0.2.0-beta](https://github.com/Kortexio/ContextMemory/compare/v0.1.0-beta...v0.2.0-beta) (2026-09-28)


### Features

* **agentic:** raise wiki call budget to 5 distinct queries ([f433fb8](https://github.com/Kortexio/ContextMemory/commit/f433fb810cb376c1aa53ee8d88839df4d37c1edb))
* bundle llama.cpp and vLLM compose overrides with an end-to-end memory check ([106f718](https://github.com/Kortexio/ContextMemory/commit/106f71866f1e6bf28d8f19a4f89a000bef7e778e))
* self-host launch readiness (llama.cpp / vLLM, e2e memory check, cleaner public signals) ([257c78c](https://github.com/Kortexio/ContextMemory/commit/257c78ca681c819e137fd8a858c1d8c21c716810))


### Bug Fixes

* **agentic:** distinguish wiki budget controls from failures ([0b0a015](https://github.com/Kortexio/ContextMemory/commit/0b0a0157d1ee8f555d2e517aef9893b633391c08))
* **agentic:** reject repeated response sections ([c5d50f6](https://github.com/Kortexio/ContextMemory/commit/c5d50f6786fba343793fd1cffc8ddfe0bb7086c2))
* **mcp:** NDJSON framing for azure-monitor-mcp ([0e6163a](https://github.com/Kortexio/ContextMemory/commit/0e6163ae8880e3ef27b7d8f26a73374c959fed55))
* **mcp:** speak NDJSON in azure-monitor-mcp for mcp-runtime ([4d0cc3f](https://github.com/Kortexio/ContextMemory/commit/4d0cc3f1fee9541a87b72f812bd54f7e124e6c76))
* **migrations:** cast ConfigJson updates to jsonb. ([48f0678](https://github.com/Kortexio/ContextMemory/commit/48f0678c1f10b97b6acb004c0e36d76e24480411))
* prevent ArgumentOutOfRange on agentic string truncation ([62ee36a](https://github.com/Kortexio/ContextMemory/commit/62ee36aae77eb83a3c557ce63dc066b225cd3fa6))
* prevent ArgumentOutOfRange on agentic string truncation. ([90c80bc](https://github.com/Kortexio/ContextMemory/commit/90c80bcf1f5ea202c2b4e22b5b9430c7a96708a9))

## [0.1.0-beta](https://github.com/Kortexio/ContextMemory/compare/v0.0.1-beta...v0.1.0-beta) (2026-08-15)


### Features

* add Cursor-style HTTP, vision, browser, PDF, and canvas agent tools. ([0d322c2](https://github.com/Kortexio/ContextMemory/commit/0d322c24173b36fb4dabfc9957471d5d689f4fe7))
* derive LLM protocol capabilities from prompt profiles. ([bd159ad](https://github.com/Kortexio/ContextMemory/commit/bd159adfcda36302c010b99ef2f11e0363631f13))
* expand agentic guardrail catalog with enable/disable kinds ([4fabaec](https://github.com/Kortexio/ContextMemory/commit/4fabaec99f4e05b221c00d85fb214cd68d63396c))
* expose LLM generation defaults in Admin and apply to chat/generate payloads ([e459575](https://github.com/Kortexio/ContextMemory/commit/e4595750e0138a24ab6a522061f35b7629aaf642))
* multi-model harness, live-data guardrail, and Ollama num_ctx fix. ([7244aa2](https://github.com/Kortexio/ContextMemory/commit/7244aa26f2b253663b263c9ff9ee1577b9ca578b))


### Bug Fixes

* **ci:** stop GITHUB_TOKEN from overriding PEER_SYNC_TOKEN on mirror push ([f230ff2](https://github.com/Kortexio/ContextMemory/commit/f230ff2f3787fdbdaac8cfe6db0e617b49a184e1))
* coerce query_objects filter object/string into string array. ([95069ee](https://github.com/Kortexio/ContextMemory/commit/95069ee1500c6d82a899d46f0d925cda1529084e))
* map fieldsToReturn alias in query_objects normalizer. ([9b3e172](https://github.com/Kortexio/ContextMemory/commit/9b3e17236ba2fb8c984cb539795a96c15197462f))
* pin LinkedIn API version and clarify dev.to errors ([dceafbc](https://github.com/Kortexio/ContextMemory/commit/dceafbc493273723a91b74e9a9259e75a0949f75))
* promote prose MCP tool JSON into structured tool_calls. ([5ae1b84](https://github.com/Kortexio/ContextMemory/commit/5ae1b840c3e0b19e66aa96d1e043d86048451f79))
* publish thin helpers under available Kortexio package names ([fbff059](https://github.com/Kortexio/ContextMemory/commit/fbff059c89ffbbc94b24329510fd7e376c9387f9))
* regenerate AddSkillActivation migration with model snapshot. ([91a53c5](https://github.com/Kortexio/ContextMemory/commit/91a53c5d22890a54eeaf54a80dbdc3bd4a2781b7))
* rewrite SQL-style query_objects filters to Zuora field.OP:value. ([a145663](https://github.com/Kortexio/ContextMemory/commit/a145663c4a05de0598cfdbc1205cc5833e19d50b))
* set User-Agent on dev.to announcer requests ([88e4992](https://github.com/Kortexio/ContextMemory/commit/88e49927cb17e50d250078d79775f5812bfd09d0))
* stabilize Qwen client-side tools and stop capping Weak MCP catalogs. ([c4db686](https://github.com/Kortexio/ContextMemory/commit/c4db6861fc5c165afa520433867b2f330a9f98c1))
* stop live-data validation loops and soften noisy guardrail defaults ([14f262a](https://github.com/Kortexio/ContextMemory/commit/14f262ae6e3823a72fd50d28334245d5a365c5b2))
* stop RequireZeroExitCode guardrail from looping forever on retried tool failures ([1cb6155](https://github.com/Kortexio/ContextMemory/commit/1cb6155d72120651ac27ecc4f8c90c29fa432831))
* stop tool_describe failures from poisoning agentic validation loops. ([3fcff28](https://github.com/Kortexio/ContextMemory/commit/3fcff2894ca872e9016e0488466b29fb93f5ecea))
* tighten LinkedIn release posts to messaging voice ([beb0894](https://github.com/Kortexio/ContextMemory/commit/beb0894f10f87ce1ff72be4e0400e1b0fa8abe4d))
