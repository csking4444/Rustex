# Rustex — workspace guide

Two related codebases live here:

- **`server/` + `client/` + `database/`** — the full Rustex app. ASP.NET Core 8 Web API
  (Discord OAuth2, JWT + rotating refresh, SignalR hub at `/hubs/dashboard`, Serilog,
  Redis) against PostgreSQL, with a React 18 + TypeScript + Vite + Tailwind frontend.
  Run it with `docker-compose.yml`. See `README.md` and `docs/ROADMAP.md` for what is
  real vs. stubbed per phase — that file is the source of truth on build status.
- **`rustex-site.html`** — the standalone marketing site + gated dashboard, deployed
  separately. See "Site workflow" below. It is untracked in this repo on purpose.

## Working style

**Caveman mode is on by default.** Terse, compressed output; full technical accuracy.
Skill lives at `~/.claude/skills/caveman`. Default intensity `full`; switch with
`/caveman lite|full|ultra`. Turn off only when asked ("stop caveman" / "normal mode").

**Headroom** (`headroom-ai`) handles token/context optimization. It is a local proxy —
it only does anything once it is running *and* the client is routed through it:

```
headroom proxy          # start it (port 8787)
headroom doctor         # verify routing; expect proxy=ok, claude=routed
headroom savings        # confirm compression is actually happening
```

If `doctor` reports `not routed`, Claude Code is talking to the API directly and
headroom is doing nothing regardless of it being installed.

**Not used here:** Fireworks. Do not add it to the workspace or workflow.

## Site workflow (`rustex-site.html`)

Single-file static site: inline `<style>`, one `<script>` block, images embedded as
base64 WebP data URIs so the page makes no external requests.

Edit `C:\claude\rustex-site.html`, then copy it to `C:\rustex-site\index.html` before
committing. That second directory is the deploy repo
(`github.com/csking4444/rustex-site`), which auto-deploys to Vercel at
`rustex-site.vercel.app`.

The file is ~1.3 MB — too large to read whole. Use `Grep` to locate a region and
`Read` with `offset`/`limit`. When patching with a script, **bound the replacement on
both ends and assert the tail survived** (`'</script>' in tail`) before writing; an
unbounded `find()` once silently truncated 29 KB of JS.

### Hard constraints on the site

- **No fabricated data.** No invented accounts, servers, invoices, cards, billing
  dates, or telemetry. Only real data, or content explicitly labelled as an
  example/demo. This is a real product, not a mockup.
- **Real Steam OpenID only.** No simulated or fake login path, ever.
- Pricing: Scout $4.99 (2 paired servers) · Raider $9.99 · Clan $19.99.

### Site backend (`C:\rustex-site\api\`)

Vercel serverless functions, Node ESM, zero dependencies.

- `_lib.js` — HMAC-SHA256 signed HttpOnly session cookie, comped-plan lookup, origin
  resolution. Everything **fails closed**: a missing `SESSION_SECRET` returns
  `null`/`false` rather than throwing, so it can never become a bare
  `FUNCTION_INVOCATION_FAILED`.
- `auth/steam.js` → redirect to Steam. `auth/callback.js` → **must** re-POST every
  `openid.*` param back to Steam with `openid.mode=check_authentication`; skipping
  that lets anyone forge a `claimed_id`. Whole handler is wrapped in try/catch.
- `auth/me.js` — the only identity + entitlement source the client trusts. The client
  never asserts its own plan.
- `auth/health.js` — reports config **presence only**, never values and never which
  accounts are comped. Safe to open in a browser.

Env vars: `SESSION_SECRET` (required), `STEAM_API_KEY` (optional — adds name/avatar),
`PUBLIC_ORIGIN` (optional), `COMPED_ACCOUNTS` (`"steamid:plan,steamid:plan"`).

`COMPED_ACCOUNTS` is a manual grant, **not** a subscription — no payment provider is
wired up. Render it as "Complimentary access" / "Comped — $0", never as fake billing
dates.

Vercel Deployment Protection must stay **off**: it blocks Steam's callback for real
users, not just for testing.
