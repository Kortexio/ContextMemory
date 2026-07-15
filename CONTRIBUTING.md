# Contributing

## Language

- **English** for source code, comments, logs, HTTP errors, Admin UI, README, and config templates.
- **Tenant locale** (`DefaultLanguage`, `WikiSchema`, personas) for LLM-facing prompts, wiki text, HITL keywords, and tool outputs returned to the model.
- Localized strings live in `src/ContextMemory.Core/Localization/` (`TenantLocale`, `AgenticMessages`, `ToolExecutionMessages`, `ValidationMessages`, `LlmPrompts`, `ToolSchemaMessages`). Use `TenantLocale.Select(language, en, pt)` for new user-facing or model-facing text.

## Development setup

```bash
dotnet restore
dotnet build ContextMemory.sln
dotnet test tests/ContextMemory.Api.Tests/ContextMemory.Api.Tests.csproj
```

Copy `appsettings.Development.example.json` to `appsettings.Development.json` (gitignored) or use `.env.example` for environment variables.

## Documentation

Public contracts are in `src/ContextMemory.Core/Contracts/` with XML summaries. When adding or changing an interface, update the `///` comments and keep Swagger descriptions in English.

## Pull requests

1. Keep changes focused; match existing naming and DI patterns.
2. Run the full test suite before opening a PR.
3. Do not commit secrets, `data/`, or local `appsettings.Development.json`.
