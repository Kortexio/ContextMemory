# Changelog

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
