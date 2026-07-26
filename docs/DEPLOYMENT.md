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

Vercel doesn't run ASP.NET Core apps (it hosts static sites, Node/Next.js, and serverless functions in a handful of languages — not full .NET web servers with background workers, SignalR, and a Postgres/Redis connection). The API needs a host that runs long-lived processes. Reasonable options, roughly cheapest/simplest to most involved:

- **Fly.io** or **Railway** — both support deploying an arbitrary Docker image (the repo already has `server/src/Rustex.Api/Dockerfile`), include a free/cheap tier, and can host Postgres+Redis alongside it.
- **Azure App Service** (Linux, .NET runtime stack) — first-party .NET hosting, integrates cleanly with Azure Database for PostgreSQL / Azure Cache for Redis if you want managed data stores.
- **A VPS** (DigitalOcean, Hetzner, etc.) running `docker compose up` from this repo directly — most control, most setup.

Whichever you pick, update:
- `Discord__RedirectUri` / `Discord__FrontendCallbackUrl` (Discord OAuth callback + where to send the browser after login) to the real deployed URLs
- `Cors__AllowedOrigins__0` to the Vercel frontend URL, so the browser is allowed to call the API cross-origin
- The Vercel `VITE_API_BASE_URL`/`VITE_HUB_URL` env vars (above) to point at wherever this ends up

None of this is wired up yet — Phase 10 in [ROADMAP.md](ROADMAP.md) covers hardening the actual deploy path once a backend host is chosen.
