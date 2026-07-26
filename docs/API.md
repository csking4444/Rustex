# API Reference

Base URL (dev): `https://localhost:5443/api`

Only implemented endpoints are listed. Everything else in the original spec's API surface (notifications, maps, analytics, PSTN call alerts) is scoped for later phases and will be documented here as it lands.

## Auth

| Method | Path | Description |
|---|---|---|
| GET | `/auth/discord/login` | Redirects to Discord OAuth2 authorize URL |
| GET | `/auth/discord/callback` | OAuth2 callback; exchanges code, upserts user, issues JWT + refresh token, redirects to frontend with tokens |
| POST | `/auth/refresh` | Body: `{ refreshToken }` → new access + refresh token pair (rotates the refresh token) |
| POST | `/auth/logout` | Revokes the current refresh token/session |
| GET | `/users/me` | Current authenticated user + profile (requires `Authorization: Bearer <token>`) |

## Servers

| Method | Path | Description |
|---|---|---|
| GET | `/servers` | List servers owned by the current user, including each server's latest live-status snapshot (`pingMs`, `playerCount`, `maxPlayers`, `queueSize`, `lastPolledAt`) |
| POST | `/servers` | Create a server entry |
| GET | `/servers/{id}` | Get one server, with the same live-status fields |
| PUT | `/servers/{id}` | Update a server entry |
| DELETE | `/servers/{id}` | Remove a server entry |

Live status comes from `ServerStatusPollingWorker`, which queries each server's query port via A2S_INFO (the Source engine query protocol used by the Steam server browser) every 20 seconds — this is real data from any publicly reachable Rust server, not a stub. `queryPort` must be set on the server for polling to run; `queueSize` is always null today since A2S_INFO doesn't expose it.

## Teams (stub CRUD)

| Method | Path | Description |
|---|---|---|
| GET | `/teams` | Teams the current user belongs to |
| POST | `/teams` | Create a team (creator becomes Owner) |

## Raid Events (simulated event source — see note below)

| Method | Path | Description |
|---|---|---|
| GET | `/raid-events/recent?limit=20` | Most recent raid events across the current user's servers |

## Raid Alarm Settings

| Method | Path | Description |
|---|---|---|
| GET | `/servers/{serverId}/raid-alarm-settings` | Current thresholds/window/cooldown for a server (defaults if never configured) |
| PUT | `/servers/{serverId}/raid-alarm-settings` | Update thresholds — `isEnabled`, `tier1Threshold`/`tier2Threshold`/`tier3Threshold` (must be non-decreasing), `timeWindowSeconds`, `clusterRadius`, `cooldownSeconds` |

## Health

| Method | Path | Description |
|---|---|---|
| GET | `/health` | Liveness/readiness (DB + Redis checks) |
| GET | `/version` | Build/phase info |

## Real-time — SignalR hub `/hubs/dashboard`

Connect with `?clientKind=app` (installed/standalone PWA) or omit it (plain browser tab) — see `client/src/lib/clientKind.ts`. Authenticated connections auto-join a per-user group in addition to whatever server groups they `Subscribe` to.

- Client → server: `Subscribe(serverId)`, `Unsubscribe(serverId)` — joins/leaves the `server:{id}` group
- Server → client, per server: `RaidEventCreated`, `ServerStatusUpdated`
- Server → client, per user: `IncomingRaidCall` (App-kind connections — full-screen ring alert), `RaidAlertNotification` (Desktop-kind connections — plain browser notification)

(Payload shapes in `Hubs/SignalRRaidEventBroadcaster.cs`.)

`EventIngestionWorker` (in `Rustex.Infrastructure`) consumes `SimulatedEventSource` for every registered server, clusters candidate events via `RaidAlarmEvaluator`, persists a `RaidEvent` + broadcasts `RaidEventCreated` for clusters reaching at least Tier 1, then hands off to `EmergencyAlertDispatcher` to notify the server owner. The event *source* is simulated; the clustering/tiering/dispatch logic downstream of it is real. Disable the simulator with `Ingestion__EnableSimulator=false`.

All authenticated endpoints expect `Authorization: Bearer <accessToken>`. Access tokens are short-lived (15 min default); use `/auth/refresh` to rotate.
