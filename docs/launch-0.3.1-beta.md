# Launch kit — ContextMemory 0.3.1-beta

Ship target for this GTM pass is **`v0.3.1-beta`** (not 1.0 yet): self-host Docker/GHCR, memory + agent loop on one node, BYO OpenAI-compatible LLM, Admin + MCP, stable `/v1` + Admin HTTP docs.

## Channels

1. **Show HN** — paste [`show-hn.md`](show-hn.md). Post manually on HN the day you want traffic (API cannot auto-post).
2. **LinkedIn / dev.to** — `.github/workflows/announce-release.yml` (soft-fails if tokens expire).
3. **Cloud CTA** — secondary only (link at bottom of Show HN / README), never the hero.

## AGPL

Do **not** lead with copyleft. Link [LICENSE](../LICENSE) and/or Cloud if someone asks about commercial/hosted.

## Post-GA tickets

- Mid-loop stream: [#24](https://github.com/Kortexio/ContextMemory/issues/24)
- Distributed rate limits: [#25](https://github.com/Kortexio/ContextMemory/issues/25)
- Admin SSO: [#26](https://github.com/Kortexio/ContextMemory/issues/26)

## Checklist before posting

- [x] Tag `v0.3.1-beta` + GHCR
- [x] Changelog with A/B numbers (no internal LLM host)
- [x] README: Weak model. Strong agent loop. / header helpers (not SDK)
- [x] `docs/api.md` freeze language; no “APIs may still move” on the main pitch
- [x] Announce workflow soft-fails LinkedIn/dev.to auth gaps
- [ ] Human: paste Show HN when ready ([show-hn.md](show-hn.md))
- [ ] Human: refresh LinkedIn / npm org / PyPI tokens if announce/publish warned
