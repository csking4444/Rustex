# Rustex

**Rustex** is a premium, original companion application for Rust server communities — real-time raid alarms, live event tracking, team coordination, interactive maps, analytics, and multi-channel emergency notifications (including automated phone call escalation).

This project is an independent implementation, inspired only by the *category* of tool it belongs to (Rust server companion / raid-alarm utilities). No proprietary code, assets, or branding from any existing application were copied. All code, UI, architecture, and branding here are original.

> ⚠️ **Build status: Phase 1 + 2 done, Phase 3 + part of Phase 4 in progress.** See [docs/ROADMAP.md](docs/ROADMAP.md) for the full phase-by-phase status.

---

## What's implemented

- [x] Repo structure, Docker Compose, CI skeleton
- [x] PostgreSQL schema for the full domain model (users, teams, servers, raid events, notifications, phone alerts, analytics, etc.)
- [x] ASP.NET Core 8 Web API: Discord OAuth2 login, JWT access tokens + rotating refresh tokens, role-based authorization, Redis-backed caching/session store, Serilog logging, rate limiting, security headers, health checks
- [x] SignalR hub for real-time push (`/hubs/dashboard`), consumed live by the frontend
- [x] **Real live server status** (Phase 2): `A2sQueryClient` speaks the Source engine A2S_INFO protocol to each server's query port — genuine ping/player-count/map data from any public Rust server, no plugin or Rust+ pairing required. `ServerStatusPollingWorker` polls every 20s and pushes updates over SignalR.
- [x] **Tier-based raid alarm evaluation** (Phase 3): `RaidAlarmEvaluator` clusters events by time window + radius and classifies each cluster into `Tier1`/`Tier2`/`Tier3` by explosion/notification count (defaults: 1+/3+/5+), all per-server and adjustable via the Raid Alerts page or `/api/servers/{id}/raid-alarm-settings`.
- [x] **Platform-aware emergency alerts** (Phase 4, partial): a qualifying raid triggers a full-screen ring alert (installed/standalone PWA) or a plain browser notification (regular desktop tab) — see the ingestion note below for what's real vs. stubbed here.
- [x] React 18 + TypeScript + Tailwind + Framer Motion + React Query + React Router frontend: tactical theme, Discord login flow, collapsible sidebar shell, notification drawer, dashboard with live server/raid cards, ring-alert overlay

## What's *not* built yet

Rust team chat automation, interactive map, real PSTN phone call escalation (Twilio/etc.), analytics engine, full team management, and the actual Rust+ Smart Alarm listener (currently an honest stub) are designed for in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) and scheduled in [docs/ROADMAP.md](docs/ROADMAP.md), but not implemented.

---

## ⚠️ Important: Rust event data source

Rust has no public API for per-explosion raid detection. This project was scoped to use the **official Rust+ companion protocol** for live data. Rust+ only exposes a limited event set (server population, map, some monument timers via device pairing) — it does **not** expose explosions, weapon types, or combat log data. That means:

- The full "Advanced Raid Alarm System" described in the original spec (explosion counting, weapon detection, raid clustering) **cannot be powered by real data through Rust+ alone**.
- The ingestion layer is built around an `IEventSource` abstraction specifically so a richer source — most realistically a small Oxide/Carbon **server plugin** pushing webhook events — can be added later without changing the rest of the system.
- For local development and demoing the raid-alarm UI/pipeline end-to-end, a `SimulatedEventSource` generates synthetic raid events on a timer.

See [docs/ARCHITECTURE.md#event-ingestion](docs/ARCHITECTURE.md) for details.

---

## ⚠️ Important: "emergency call" is a ring alert, not a real phone call

The original spec described PSTN phone calls via Twilio/Vonage/Plivo. What's implemented instead — because it's buildable and verifiable without third-party call credentials — is platform-aware in-app alerting:

- **Installed/standalone PWA** ("App"): a full-screen ring alert with a synthesized siren and vibration.
- **Regular browser tab** ("Desktop"): a plain browser notification.

Neither is a real VOIP/telephony call. A browser or PWA cannot register with iOS/Android's native call stack (CallKit/ConnectionService) — that needs a native app shell, which this repo doesn't build. It won't ring through silent mode or show a system call UI. The trigger side has the same kind of gap: real Smart Alarm notifications require reproducing Rust+'s undocumented FCM pairing handshake (`IRustPlusNotificationListener` is a documented stub for this), so today the same simulated pipeline that stands in for explosions also stands in for alarm pings. `PhoneNumber`/`CallAlertSetting`/`CallAlert`/`IVoiceCallProvider` are still in the schema for a real PSTN channel later. See [docs/ARCHITECTURE.md#emergency-alerts-phase-4](docs/ARCHITECTURE.md) for the full breakdown.

---

## Tech stack

| Layer | Technology |
|---|---|
| Frontend | React 18, TypeScript, Vite, Tailwind CSS, Framer Motion, React Query, React Router |
| Backend | ASP.NET Core 8 Web API, C# |
| Database | PostgreSQL 16 (via EF Core / Npgsql) |
| Cache / Sessions | Redis 7 |
| Real-time | SignalR |
| Auth | Discord OAuth2, JWT + rotating refresh tokens |
| Infra | Docker, Docker Compose, Nginx, GitHub Actions |

## Project layout

```
/client     React + TypeScript frontend
/server     ASP.NET Core backend (Domain / Infrastructure / Api / Tests)
/database   SQL schema reference + seed data
/docker     Nginx + container configs
/docs       Architecture, roadmap, API notes
/scripts    Dev setup helpers
/.github    CI workflows
```

## Getting started

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- [Node.js 20+](https://nodejs.org/)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (for Postgres/Redis, or the full stack)
- A Discord application (for OAuth) — create one at https://discord.com/developers/applications

### 1. Configure environment

```bash
cp server/src/Rustex.Api/.env.example server/src/Rustex.Api/.env
cp client/.env.example client/.env
```

Fill in your Discord OAuth client ID/secret and a JWT signing secret (see comments in each `.env.example`).

### 2. Start infrastructure (Postgres + Redis)

```bash
docker compose up -d postgres redis
```

### 3. Apply the database schema

The EF Core model lives in `server/src/Rustex.Domain` / `Rustex.Infrastructure`. Generate and apply the initial migration locally (this repo ships the reference SQL in `database/schema.sql` but not a pre-baked EF migration, since it must be generated by the .NET SDK):

```bash
cd server/src/Rustex.Api
dotnet tool install --global dotnet-ef   # if you don't have it
dotnet ef migrations add InitialCreate -p ../Rustex.Infrastructure -s .
dotnet ef database update -p ../Rustex.Infrastructure -s .
```

### 4. Run the backend

```bash
cd server/src/Rustex.Api
dotnet run
```

API listens on `https://localhost:5443` (see `appsettings.Development.json`). Swagger UI is available at `/swagger` in development.

### 5. Run the frontend

```bash
cd client
npm install
npm run dev
```

Frontend runs on `http://localhost:5173` and proxies API calls to the backend.

### Or: run everything with Docker Compose

```bash
docker compose up --build
```

This brings up Postgres, Redis, the API, the frontend (served via Nginx), and applies migrations on API startup in the `Development`/`Docker` environment.

## Testing

```bash
# backend
cd server && dotnet test

# frontend
cd client && npm run test
```

## Documentation

- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — system design, module boundaries, event ingestion design
- [docs/ROADMAP.md](docs/ROADMAP.md) — phase-by-phase build plan
- [docs/API.md](docs/API.md) — REST + SignalR endpoint reference for what's implemented so far
- [database/schema.sql](database/schema.sql) — reference relational schema

## License / originality note

All code, component names, color tokens, and copy in this repository are original and written for this project. No source code, textures, icons, or branding were copied from any third-party application. Contributors should keep it that way — if you're porting an idea from another tool, reimplement the *behavior*, not the *code*.
