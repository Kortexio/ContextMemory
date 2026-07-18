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

### Run API + Admin UI

```bash
# Terminal 1 — gateway
cd src/ContextMemory.Api
dotnet run
# http://localhost:5100

# Terminal 2 — Admin console / Chat Lab
cd src/ContextMemory.Admin.Web
dotnet run
# http://localhost:5200
```

In Admin Settings, set API base URL `http://localhost:5100` and the Master Key from `ContextMemory:MasterKey` (User Secrets, env, or `appsettings`).

Local secrets — prefer User Secrets or environment variables (see [`.env.example`](.env.example)). You may also create a gitignored `src/ContextMemory.Api/appsettings.Development.json` with local overrides.

## Documentation

Public contracts are in `src/ContextMemory.Core/Contracts/` with XML summaries. When adding or changing an interface, update the `///` comments and keep Swagger descriptions in English.

## Pull requests

1. Keep changes focused; match existing naming and DI patterns.
2. Run the full test suite before opening a PR.
3. Do not commit secrets, `data/`, or local `appsettings.Development.json`.
