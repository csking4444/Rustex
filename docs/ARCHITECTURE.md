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
- Still missing: the Twilio/etc. PSTN channel as a true "your phone rings even if this device is off" fallback (Web Push, below, now covers the "device is on but the app is closed" case).

**Web Push — reaching a closed app.** SignalR only reaches a live WebSocket connection; `PushSubscription` (one row per browser, via the standard `PushManager.subscribe()` API) plus VAPID (RFC 8292, via the `WebPush` NuGet package) close that gap. `EmergencyAlertDispatcher` sends to every subscription a user has whenever `UserSettings.PushEnabled` is on, regardless of `ClientConnectionRegistry` state — that's the entire point of Push vs. SignalR. `client/public/sw.js`'s `push` handler shows a `Notification` with `requireInteraction: true`; `notificationclick` focuses an existing tab or opens one. VAPID keys are optional config (`WebPush__PublicKey`/`PrivateKey`) — `IWebPushSender.IsConfigured` gates everything so an unconfigured server just silently skips this channel rather than failing.

**Quiet hours.** `UserSettings.QuietHoursStart/End` (plain `TimeOnly?`, compared in `QuietHoursTimezone` via `TimeZoneInfo.ConvertTime`, with midnight-wraparound handling) mutes the *ring* specifically — `EmergencyAlertDispatcher` still sends a plain notification instead of going silent. The API exchanges these as `"HH:mm"` strings, not the framework's default `TimeOnly` JSON format, specifically to avoid depending on exactly how System.Text.Json's built-in `TimeOnly` converter formats sub-second precision — parsing/formatting is done explicitly in `UserSettingsController` instead.

**Discord webhooks.** Unlike Rust+/Steam, Discord's incoming-webhook format is fully public and simple (a POST of `{ embeds: [...] }`) — `IDiscordWebhookSender` is a complete implementation, not a stub. `Webhook` rows are scoped per server; `EmergencyAlertDispatcher` posts to every active one tagged for `RaidDetected` when `UserSettings.DiscordEnabled` is on.

**Notification center.** `NotificationsController` exposes the `Notification` rows `EmergencyAlertDispatcher` already writes (list, unread count, mark-read/mark-all-read). The frontend polls the unread count every 60s and also invalidates it immediately on the `IncomingRaidCall`/`RaidAlertNotification` SignalR events, so the sidebar bell badge and drawer stay current without a dedicated "notification created" broadcast.

## Team chat automation (Phase 5)

`TemplateRenderer` (`Rustex.Domain.Templating`) is, like the raid evaluator, pure logic — placeholder substitution over a fixed set (`{server} {grid} {time} {event} {player} {count} {team} {weapon}`) with no framework dependency. `MessageTemplate` rows (one per team+event, optionally scoped to a single server) are managed via `MessageTemplatesController`; `ChatTemplateMetadataController` exposes the supported event-type catalog (`ChatEventTypes`) and a preview endpoint that renders a template against sample data. What's *not* built: anything that actually sends a rendered template into in-game team chat — that needs the same bridge (Rust+ pairing or a plugin) the raid-detection gap depends on, so the automation UI is fully real but currently automates nothing downstream.

## Interactive map (Phase 6)

`MapData` is created lazily, one row per server, the first time its map or markers are requested (`MapController.GetOrCreateMapAsync` — same lazy-default pattern as `RaidAlarmSettings`). Markers (`Marker`: type/x/y/label/color/isShared) are plain CRUD scoped to that map. The frontend's `InteractiveMap` is a custom SVG viewer using the SVG element's native `viewBox` for zoom/pan state and `getScreenCTM().inverse()` for exact screen-to-world coordinate conversion on click/drag — deliberately not MapLibre or any tile-based library, because there is no real tile source: Rust's terrain is procedurally generated per-seed and Facepunch exposes no map-imagery API. `MapData.ImageUrl` is reserved for a server-supplied image (e.g. from a community map-render service) to be layered in behind the grid later.

## Team features (Phase 7)

Team creation seeds three system `TeamRole` rows (Owner/Admin/Member); the creator becomes Owner. `TeamInvitesController` issues random-token invites (7-day expiry) accepted via a token-only endpoint (`TeamInviteAcceptanceController`, not nested under a team route since the accepting user isn't a member yet) that adds the accepter with the Member role. `TeamMembersController` lets the Owner change roles or remove members, and lets any member remove themselves. The `Permission`/`team_role_permissions` tables from the Phase 1 schema exist but are unseeded and unenforced — today's authorization is "Owner can do X" checks in each controller, not a real permission matrix.

## Analytics (Phase 8)

`AnalyticsController` computes everything on demand from `RaidEvents`/`ServerStatusSnapshots` rather than reading from a precomputed `AnalyticsSnapshot` table. This is a deliberate simplification, not an oversight: `Count`/`Average`/`Max` are simple aggregates that reliably translate to SQL across EF Core/Npgsql versions (unlike the "first row per group" pattern called out in `ServersController`'s snapshot-join comment), and computing live means the numbers are never stale. Day/hour bucketing happens in-memory after a single bounded fetch (`WHERE ServerId = ... AND DetectedAt >= cutoff`), which is fine at the data volumes this app has today. `AnalyticsSnapshot`-based rollups remain the right move once raid volume makes scanning raw rows per request expensive — see [ROADMAP.md](ROADMAP.md) Phase 9.

## Security

- Discord OAuth2 for identity; the app never sees or stores Discord passwords.
- JWT access tokens (short-lived) + rotating refresh tokens (hashed at rest, stored server-side so they can be revoked).
- Role/permission tables allow per-team roles (owner/admin/member) distinct from any future system-level roles.
- Rate limiting middleware on all `/api` routes; stricter limits on `/auth/*`.
- Security headers middleware (HSTS, X-Content-Type-Options, X-Frame-Options, CSP baseline).
- All phone numbers and OAuth tokens are stored encrypted at rest (see `docs/ROADMAP.md` Phase 4 for the encryption-at-rest implementation of `PhoneNumber`).

## Real-time

SignalR hub `DashboardHub` (`/hubs/dashboard`) pushes updates to connected clients over two group types: per-server (`server:{id}`, via `Subscribe`/`Unsubscribe`) for `RaidEventCreated`/`ServerStatusUpdated`, and per-user (`user:{id}`, auto-joined on connect for authenticated users) for `IncomingRaidCall`/`RaidAlertNotification`. `EventIngestionWorker` still drives the server-side data using `SimulatedEventSource`, but the clustering/tiering/dispatch logic downstream of it (Phases 3-4, above) is real.
