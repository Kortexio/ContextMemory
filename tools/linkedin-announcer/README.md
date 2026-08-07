# LinkedInAnnouncer

.NET 9 console tool that posts ContextMemory release announcements to LinkedIn.

## Modes

```bash
# One-shot OAuth (local)
export LINKEDIN_CLIENT_ID=...
export LINKEDIN_CLIENT_SECRET=...
dotnet run --project tools/linkedin-announcer -- get-token

# CI / local post
export DRY_RUN=true
export RELEASE_TAG=v1.2.0
export RELEASE_BODY="- feat: ..."
export REPO_URL=https://github.com/Kortexio/ContextMemory
# + LINKEDIN_* secrets
dotnet run --project tools/linkedin-announcer -- post
```

Post format follows `blueprint/MESSAGING.md` (short hook + summary + CTA). Optional `LINKEDIN_ORG_URN` posts as the company page.
