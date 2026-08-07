# XAnnouncer

Posts a short ContextMemory release tweet via X API v2 (OAuth 1.0a user context).

```bash
export DRY_RUN=true
export RELEASE_TAG=v1.2.0
export RELEASE_BODY="- feat: ..."
export REPO_URL=https://github.com/Kortexio/ContextMemory
# X_API_KEY X_API_SECRET X_ACCESS_TOKEN X_ACCESS_SECRET
dotnet run --project tools/x-announcer -- post
```
