# Architecture

## Overview

Rustex is a modular monolith today, split into clearly separated layers so pieces (notably the notification/voice-call system and event ingestion) can be extracted into standalone services later without a rewrite.

```
┌─────────────────────────────────────────────────────────────┐
│                        client (React)                        │
│   Sidebar shell, Dashboard, Servers, Maps, Alerts, Teams...   │
│   React Query <-> REST      SignalR client <-> real-time hub  │
└───────────────┬───────────────────────────┬───────────────────┘
                │ HTTPS/JSON                 │ WebSocket
┌───────────────▼───────────────────────────▼───────────────────┐
│                     Rustex.Api (ASP.NET Core)                  │
│  Controllers · SignalR Hubs · Middleware (auth, rate limit,    │
│  security headers) · Background workers                        │
└───────────────┬───────────────────────────┬───────────────────┘
                │                            │
┌───────────────▼───────────────┐  ┌─────────▼───────────────────┐
│  Rustex.Infrastructure         │  │   Rustex.Domain              │
│  EF Core / Npgsql, Redis,      │  │   Entities, enums, DTOs,     │
│  Discord OAuth, JWT, event     │  │   IEventSource abstraction,  │
│  ingestion sources             │  │   no framework dependencies  │
└───────────────┬────────────────┘  └───────────────────────────────┘
                │
      ┌─────────▼─────────┐   ┌─────────────┐
      │ PostgreSQL         │   │ Redis        │
      │ (system of record) │   │ (cache/queue │
      │                     │   │  /sessions)  │
      └─────────────────────┘   └─────────────┘
```

### Why this split

- **Domain** has zero framework dependencies — entities and interfaces only. This is what "clean architecture" buys us: the raid-detection/notification logic can be unit-tested without a database or web server, and swapped implementations (e.g. a new voice provider, a new event source) only touch Infrastructure.
- **Infrastructure** implements Domain interfaces: EF Core repositories/DbContext, Redis caching, Discord OAuth client, JWT issuance, and the event-ingestion sources.
- **Api** is thin: controllers translate HTTP/SignalR into calls against Infrastructure/Domain services, plus cross-cutting middleware (auth, rate limiting, security headers, logging).

## Event ingestion

Real-time raid/event detection needs a stream of "something exploded / spawned / a player did X" events per server. The original feature spec assumed explosion-level detail (rocket vs. C4 vs. satchel, position, count). Rust does not expose that through any first-party API — only through:

1. A server-side plugin (Oxide or Carbon) that hooks the game's C# events and forwards them (e.g. via webhook) — full detail, but requires the server owner to install something.
2. The official **Rust+** companion protocol — no plugin required, but only exposes a small set of events (server population, some monument state, pairing-based push notifications for combat log/etc. that Facepunch's own app receives) and does **not** include per-explosion telemetry.

To keep the system honest about this constraint while still letting the rest of the pipeline (raid-alarm evaluation, notification fan-out, phone escalation) be built and tested, ingestion is behind an interface:

```csharp
public interface IEventSource
{
    string Name { get; }
    IAsyncEnumerable<RaidCandidateEvent> StreamEventsAsync(Guid serverId, CancellationToken ct);
}
```

Implementations shipped in Phase 1:

- `SimulatedEventSource` — emits synthetic events on a timer for local dev/demo/tests, so the alarm pipeline, dashboard, and notification fan-out can be exercised end-to-end without a real Rust server.
- `RustPlusEventSource` — stubbed. Contains the pairing/handshake shape for the Rust+ protocol and documents exactly which events it *can* supply, but raises `NotSupportedException` for explosion-level detail so callers fail loudly instead of silently no-op'ing.

Future work (see [ROADMAP.md](ROADMAP.md)): a `PluginWebhookEventSource` that accepts authenticated webhook posts from a companion Oxide/Carbon plugin, which is the only path to real explosion-level raid detection.

Everything downstream of `IEventSource` (raid-alarm evaluation, cooldowns/sensitivity, team chat automation, notification fan-out including phone calls) is designed against the `RaidCandidateEvent` contract, not against a specific source, so adding the plugin source later is additive.

## Live server status (Phase 2)

Unlike raid/explosion telemetry, basic server status (population, map, name) *is* available from any public Rust server without a plugin or Rust+ pairing: Rust exposes the Source engine A2S_INFO UDP query protocol on its query port — the same one the Steam server browser uses. `A2sQueryClient` (`Rustex.Infrastructure.ServerQuery`) implements that protocol directly (including the challenge/response handshake), and `ServerStatusPollingWorker` polls every registered server with a query port on a 20s interval, writing `ServerStatusSnapshot` rows and broadcasting `ServerStatusUpdated`. This is real, not simulated — it's a meaningfully different trust level than the `SimulatedEventSource` raid pipeline above, and worth keeping distinct in your head: **population/map = real via A2S_INFO**, **explosions/raids = simulated until the Phase 3 plugin bridge exists**.

## Raid-alarm evaluation (Phase 3)

`RaidAlarmEvaluator` (`Rustex.Domain.RaidAlarm`) is deliberately framework-free — pure functions over `RaidCandidateEvent` and `RaidAlarmSettings` — so it's unit-testable without a database and swappable independent of the event source. Two jobs:

1. **Clustering** — `BelongsToCluster` decides if a new candidate event is close enough in time (`TimeWindowSeconds`, default 30) and, when coordinates are known, in space (`ClusterRadius`, default 50 units) to the current cluster to count as the same raid rather than starting a new one.
2. **Tiering** — `ClassifyTier` maps a finished cluster's event count to `RaidTier.Tier1/2/3` using per-server thresholds (defaults: 1+/3+/5+). This is a **count**, not a subjective severity judgment — the name is literal: however many qualifying pings landed in one cluster.

`EventIngestionWorker` (Infrastructure) owns the actual streaming/buffering/DB access/cooldown around the evaluator: it debounces each server's event stream into clusters, classifies each finished cluster, and — if it reaches at least Tier 1 and the server isn't within its `CooldownSeconds` window since the last alert — persists a `RaidEvent` and broadcasts `RaidEventCreated`. Settings are per server (`RaidAlarmSettings`, one row per server, lazily defaulted) and editable via `GET/PUT /api/servers/{id}/raid-alarm-settings`.

## Emergency alerts (Phase 4)

Two more pieces feed into and act on the same tiering pipeline:

**Trigger — Rust+ Smart Alarms.** `IRustPlusNotificationListener` is the contract for listening to the one genuinely real raid signal Rust+ provides without a plugin: in-game Smart Alarm devices push a notification (via FCM) to a paired Rust+ account when tripped. The implementation (`RustPlusNotificationListener`) is a documented stub — reproducing Facepunch's FCM pairing handshake is undocumented and needs a live Steam/Rust+ account to build and verify against (see the interface's doc comment). Once implemented, a Smart Alarm ping becomes a `RaidCandidateEvent` (`EventType = "smart_alarm"`) fed into the exact same `RaidAlarmEvaluator` as everything else — "the amount of notifications" driving tier is this, directly.

**Delivery — platform-aware, not PSTN.** The original spec described Twilio/Vonage/Plivo phone calls; that schema (`PhoneNumber`, `CallAlertSetting`, `CallAlert`, `IVoiceCallProvider`) still exists for an opt-in future channel, but the primary path implemented now is in-app, because it's buildable and testable without third-party call credentials:

- `DashboardHub` tracks each live SignalR connection's `ClientKind` (`App` if the frontend is running installed/standalone — `display-mode: standalone`, detected client-side and passed as `?clientKind=app` on connect — otherwise `Desktop`) via `IClientConnectionRegistry`, and joins a per-user group (`user:{id}`) so alerts can reach a specific user regardless of which server the raid is on.
- `EmergencyAlertDispatcher` runs after `EventIngestionWorker` persists a qualifying `RaidEvent`: resolves the server owner's `CallAlertSetting` (min tier, cooldown — separate from `RaidAlarmSettings`' *detection*-level cooldown), writes a `Notification` row, and picks a channel by the user's active `ClientKind`:
  - **App** → `IncomingRaidCall` over SignalR → the frontend's `RingAlertOverlay`: full-screen, synthesized looping siren (Web Audio API — no external audio asset), device vibration where supported.
  - **Desktop** → `RaidAlertNotification` over SignalR → a plain browser `Notification`.
- **This is not a real VOIP/telephony call**, and that's a hard platform limitation, not a shortcut: a browser or installed PWA has no API to register with iOS/Android's native telephony stack (CallKit / ConnectionService), so it can't ring through silent/DND or show a system call UI. `RingAlertOverlay` is the closest legitimate approximation achievable without shipping a native app (e.g. via a Capacitor wrapper) — loud and hard to miss while the tab/PWA is open, but not unmissable the way a real incoming call is.
- Still missing: Web Push for `App` users who are fully backgrounded or closed (SignalR only reaches live connections — `client/public/sw.js` exists but has no push handler yet), and the Twilio/etc. PSTN channel as a true "your phone rings even if this device is off" fallback.

For everything else (Discord webhooks, generic push, quiet hours), the `Notifications` / `NotificationHistory` tables are in place but the fan-out service around them is still schema-only — see [ROADMAP.md](ROADMAP.md).

## Security

- Discord OAuth2 for identity; the app never sees or stores Discord passwords.
- JWT access tokens (short-lived) + rotating refresh tokens (hashed at rest, stored server-side so they can be revoked).
- Role/permission tables allow per-team roles (owner/admin/member) distinct from any future system-level roles.
- Rate limiting middleware on all `/api` routes; stricter limits on `/auth/*`.
- Security headers middleware (HSTS, X-Content-Type-Options, X-Frame-Options, CSP baseline).
- All phone numbers and OAuth tokens are stored encrypted at rest (see `docs/ROADMAP.md` Phase 4 for the encryption-at-rest implementation of `PhoneNumber`).

## Real-time

SignalR hub `DashboardHub` (`/hubs/dashboard`) pushes updates to connected clients over two group types: per-server (`server:{id}`, via `Subscribe`/`Unsubscribe`) for `RaidEventCreated`/`ServerStatusUpdated`, and per-user (`user:{id}`, auto-joined on connect for authenticated users) for `IncomingRaidCall`/`RaidAlertNotification`. `EventIngestionWorker` still drives the server-side data using `SimulatedEventSource`, but the clustering/tiering/dispatch logic downstream of it (Phases 3-4, above) is real.
