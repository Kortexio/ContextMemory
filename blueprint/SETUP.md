# Divulgação — ContextMemory

Fonte de voz/tom: [`MESSAGING.md`](MESSAGING.md).

## Pipeline

1. **Conventional Commits** on `main` → [`.github/workflows/release-please.yml`](../.github/workflows/release-please.yml) opens a release PR (`version.txt` + CHANGELOG).
2. Merge that PR → GitHub Release published.
3. [`.github/workflows/announce-release.yml`](../.github/workflows/announce-release.yml) posts Discord + LinkedIn + X + dev.to (skips a channel if its secrets are missing).
4. [`.github/workflows/content-cadence.yml`](../.github/workflows/content-cadence.yml) (Tuesday) opens an Issue with a suggested short post from `MESSAGING.md` hooks — **manual** copy to social.

Reddit / Hacker News / Product Hunt: **manual** scripts in `MESSAGING.md` (do not automate).

## Secrets (GitHub → Settings → Secrets and variables → Actions)

| Secret | Used by |
|---|---|
| `DISCORD_WEBHOOK_URL` | Discord embed |
| `LINKEDIN_CLIENT_ID` | LinkedIn OAuth app |
| `LINKEDIN_CLIENT_SECRET` | LinkedIn |
| `LINKEDIN_REFRESH_TOKEN` | LinkedIn (re-run get-token yearly) |
| `LINKEDIN_PERSON_URN` | `urn:li:person:…` |
| `LINKEDIN_ORG_URN` | Optional company page URN |
| `X_API_KEY` / `X_API_SECRET` / `X_ACCESS_TOKEN` / `X_ACCESS_SECRET` | X API v2 OAuth 1.0a |
| `DEVTO_API_KEY` | dev.to articles |
| `NPM_TOKEN` | `publish-npm.yml` (thin TS helper) |
| `PYPI_API_TOKEN` | `publish-pypi.yml` (thin Python helper) |

## LinkedIn one-time setup

Follow [BLUEPRINT_LinkedIn_Release_Automation.md](BLUEPRINT_LinkedIn_Release_Automation.md) §11, then:

```bash
export LINKEDIN_CLIENT_ID=...
export LINKEDIN_CLIENT_SECRET=...
dotnet run --project tools/linkedin-announcer -- get-token
```

Dry run:

```bash
export DRY_RUN=true RELEASE_TAG=v0.0.0-test RELEASE_BODY="- feat: test" REPO_URL=https://github.com/Kortexio/ContextMemory
# + token env vars
dotnet run --project tools/linkedin-announcer -- post
```

Or Actions → announce-release → Run workflow → `dry_run=true`.

## X / dev.to

- X: developer.x.com app with Read and Write; user access token + secret.
- dev.to: Settings → Extensions → DEV Community API Keys.

## Release-please

- Commits: `feat:` / `fix:` / `feat!:` as usual.
- Actions permissions: **Read and write** + allow Actions to create PRs.
- `version.txt` at repo root (simple release-type).

## npm / PyPI

Workflows publish on `release: published` when tokens exist. Packages are **thin header helpers** (`v0.1.0`) — say that in marketing; do not call them full SDKs.

## Discovery checklist (manual, high leverage)

- [ ] GitHub topics from `MESSAGING.md` (include `rag-alternative`, **never** bare `rag`)
- [ ] Enable Discussions
- [ ] Social preview image (Settings → General → Social preview)
- [ ] PR to `awesome-mcp-servers`, `awesome-llm-apps`, `awesome-selfhosted`, `awesome-ai-agents`
- [ ] AlternativeTo / LibHunt / Product Hunt
- [ ] Ask stargazers: Watch → Releases only
- [ ] Capture Admin screenshots → `docs/images/` (dashboard, LLM backend dropdown, Agentic/MCP, Playground HITL, Skills)

## CI maturity

- [`.github/workflows/dotnet-tests.yml`](../.github/workflows/dotnet-tests.yml) runs `dotnet test` on push/PR — badge on README.
