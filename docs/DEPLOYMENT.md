# Deployment

## Frontend → Vercel

`client/vercel.json` is set up for this already (Vite build, SPA rewrite so client-side routing works, basic security headers).

1. Push this repo to GitHub (already done — see `git remote -v`).
2. On [vercel.com](https://vercel.com), **Add New → Project**, import the repo.
3. Set **Root Directory** to `client` (this is a monorepo — Vercel needs to know the frontend lives in a subfolder).
4. Framework preset should auto-detect as Vite; build command `npm run build`, output `dist` (already in `vercel.json` as a fallback if detection fails).
5. Add environment variables in the project settings (**Settings → Environment Variables**):
   - `VITE_API_BASE_URL` — the deployed backend's API URL, e.g. `https://api.yourdomain.com/api`
   - `VITE_HUB_URL` — the deployed backend's SignalR hub URL, e.g. `https://api.yourdomain.com/hubs/dashboard`
6. Deploy. Vercel gives you a `*.vercel.app` URL immediately; attach a custom domain under **Settings → Domains** if you want one.

Alternatively, from your own terminal (same "needs an interactive login" situation as the GitHub push — Claude Code can't complete this step for you):

```bash
cd client
npx vercel        # first run: browser login + project setup
npx vercel --prod # subsequent production deploys
```

**Local dev is unaffected** — `vite.config.ts`'s dev-server proxy (`/api`, `/hubs` → `localhost:5443`) only applies to `npm run dev`; it's irrelevant once deployed, which is why the deployed build needs the `VITE_*` env vars above instead.

## Backend → not Vercel

Vercel cannot host this API. It runs static sites and short-lived serverless functions; the API is
a long-lived ASP.NET Core process that holds **SignalR WebSockets open**, runs **background
workers** (Rust+ FCM listener, status poller, retry queue), and keeps **Postgres and Redis
connections** alive. A serverless function is killed between requests, so none of that survives.

That single constraint — a persistent process with WebSocket support — is what rules hosts in or
out. You also need Postgres and Redis reachable from it.

### Recommended: Railway

Easiest path for one person: Postgres and Redis are one-click add-ons in the same project, it
builds straight from the repo's Dockerfile, and WebSockets work with no extra configuration.
Budget roughly $5–10/month.

1. **Create the project.** railway.app → *New Project* → *Deploy from GitHub repo* → pick this
   repo. Under *Settings → Build*, set the Dockerfile path to
   `server/src/Rustex.Api/Dockerfile` and the build context to `server`.
2. **Add the datastores.** In the same project: *New* → *Database* → *PostgreSQL*, then again for
   *Redis*. Railway injects `DATABASE_URL` and `REDIS_URL`, but this app wants its own keys — set
   them explicitly in step 3.
3. **Set the environment variables** (Variables tab) — see the checklist below.
4. **Let it migrate itself.** Set `Database__AutoMigrate=true` and the app applies pending
   migrations on boot, logging what it applied. If a migration fails it refuses to start, rather
   than serving requests against a half-migrated schema that would fail later in harder-to-trace
   ways. Leave this off if you ever run more than one replica — two instances racing through the
   same migration can deadlock; migrate manually then with
   `dotnet ef database update` from a local checkout.
5. **Grab the public URL** (*Settings → Networking → Generate Domain*), e.g.
   `https://rustex-api-production.up.railway.app`.

Railway assigns the port via `$PORT`; `Program.cs` already honours it, so no change is needed.

### Alternatives

- **Fly.io** — comparable cost, `fly launch` detects the Dockerfile, set `internal_port = 8080` in
  `fly.toml`. Postgres via `fly postgres create`; Redis via Upstash. More CLI-driven than Railway.
- **Render** — same shape as Railway, managed Postgres included; Redis is a paid add-on.
- **Azure App Service** (Linux, .NET stack) — first-party .NET hosting, pairs with Azure Database
  for PostgreSQL and Azure Cache for Redis. Costs more; worth it if you are already on Azure.
- **A VPS** (Hetzner ~€4/mo, DigitalOcean) — `docker compose -f docker-compose.yml -f
  docker-compose.prod.yml up -d --build` deploys the whole stack including the nginx TLS proxy.
  Cheapest and most control; you own certificate renewal, updates and backups.

### Environment checklist

Required — the API refuses to start without these:

| Variable | Notes |
|---|---|
| `ConnectionStrings__Postgres` | `Host=…;Port=5432;Database=…;Username=…;Password=…` |
| `ConnectionStrings__Redis` | `host:6379`, plus `,password=…,ssl=True` on managed Redis |
| `Jwt__SigningKey` | `openssl rand -base64 48` |
| `Encryption__FieldKey` | `openssl rand -base64 32` — exactly 32 bytes. Rust+ credentials are encrypted with it |
| `Database__AutoMigrate` | `true` on a single-instance host, so the schema is created on first boot |

Required for the site to talk to the API at all:

| Variable | Value |
|---|---|
| `Cors__AllowedOrigins__0` | `https://rustex-site.vercel.app` — without this the browser blocks every call |
| `App__FrontendUrl` | `https://rustex-site.vercel.app` — where Stripe returns after checkout |
| `Steam__ReturnUrl` | `https://<your-api>/api/auth/steam/callback` |
| `Steam__Realm` | `https://<your-api>` |
| `Steam__FrontendCallbackUrl` | `https://rustex-site.vercel.app/` — tokens come back in the URL fragment |
| `Steam__ApiKey` | From steamcommunity.com/dev/apikey (adds name + avatar) |

Steam signs `return_to`, and the callback rejects a mismatch — so `Steam__ReturnUrl` must be the
exact public URL the browser reaches, not an internal hostname.

Billing (optional — without it the app runs and reports plans as not purchasable):

`Stripe__SecretKey`, `Stripe__WebhookSecret`, and the six
`Stripe__Prices__{scout|raider|clan}__{Monthly|Yearly}` ids. Point the Stripe webhook endpoint at
`https://<your-api>/api/billing/webhook`. See [BILLING.md](BILLING.md).

Complimentary access, if you want to grant a plan without payment:
`Billing__ComplimentaryGrants__0__SteamId`, `__Tier` (`scout`/`raider`/`clan`), `__Reason`.

### Then point the site at it

In `rustex-site.html`, set the config block near the top:

```html
<script>window.RUSTEX_API_BASE = "https://your-api-host";</script>
```

For the React client instead, set `VITE_API_BASE_URL` and `VITE_HUB_URL` in Vercel (see above).

### Verify

```bash
curl https://<your-api>/health                 # 200
curl https://<your-api>/api/billing/plans      # 200, three plans
curl -o /dev/null -w '%{http_code}\n' https://<your-api>/api/servers   # 401 — protected
```

A 401 on `/api/servers` is the correct answer, not a failure: every endpoint is authenticated by
default. If `/health` returns anything other than 200, the app did not start — check the logs for
a missing `Jwt__SigningKey` or `Encryption__FieldKey`.
