# API Reference

Base URL (dev): `https://localhost:5443/api`

Only implemented endpoints are listed. Everything else in the original spec's API surface (PSTN call alerts, escalation) is scoped for later phases and will be documented here as it lands.

## Auth

| Method | Path | Description |
|---|---|---|
| GET | `/auth/discord/login` | Redirects to Discord OAuth2 authorize URL |
| GET | `/auth/discord/callback` | OAuth2 callback; exchanges code, upserts user, issues JWT + refresh token, redirects to frontend with tokens |
| POST | `/auth/refresh` | Body: `{ refreshToken }` → new access + refresh token pair (rotates the refresh token) |
| POST | `/auth/logout` | Revokes the current refresh token/session |
| GET | `/users/me` | Current authenticated user + profile (requires `Authorization: Bearer <token>`) |
| GET | `/users/me/settings` | Notification channel toggles + quiet hours (auto-created with defaults on first access) |
| PUT | `/users/me/settings` | Update — `quietHoursStart`/`quietHoursEnd` are `"HH:mm"` strings or null (both or neither) |

## Notifications

| Method | Path | Description |
|---|---|---|
| GET | `/notifications?limit=20&unreadOnly=false` | Recent notifications for the current user |
| GET | `/notifications/unread-count` | Unread count (polled every 60s by the frontend, plus refreshed on push) |
| PUT | `/notifications/{id}/read` | Mark one as read |
| PUT | `/notifications/read-all` | Mark all as read |

## Discord Webhooks

| Method | Path | Description |
|---|---|---|
| GET | `/servers/{serverId}/webhooks` | List webhooks for a server |
| POST | `/servers/{serverId}/webhooks` | Add — body `{ url, eventTypes? }`, must be `https://`, defaults to `["RaidDetected"]` |
| DELETE | `/servers/{serverId}/webhooks/{id}` | Remove |

Fires a real Discord embed (title/description/color-by-tier) on qualifying raids — requires `discordEnabled` in user settings too.

## Web Push

| Method | Path | Description |
|---|---|---|
| GET | `/push/vapid-public-key` | Public VAPID key, or `null` if the server has none configured |
| POST | `/push/subscriptions` | Register a browser subscription — body `{ endpoint, p256dhKey, authKey }` |
| POST | `/push/unsubscribe` | Body `{ endpoint }` |

Reaches a subscribed browser even when the PWA is backgrounded or fully closed — requires `WebPush__PublicKey`/`PrivateKey` configured server-side and `pushEnabled` in user settings.

## Servers

| Method | Path | Description |
|---|---|---|
| GET | `/servers` | List servers owned by the current user, including each server's latest live-status snapshot (`pingMs`, `playerCount`, `maxPlayers`, `queueSize`, `lastPolledAt`) |
| POST | `/servers` | Create a server entry |
| GET | `/servers/{id}` | Get one server, with the same live-status fields |
| PUT | `/servers/{id}` | Update a server entry |
| DELETE | `/servers/{id}` | Remove a server entry |

Live status comes from `ServerStatusPollingWorker`, which queries each server's query port via A2S_INFO (the Source engine query protocol used by the Steam server browser) every 20 seconds — this is real data from any publicly reachable Rust server, not a stub. `queryPort` must be set on the server for polling to run; `queueSize` is always null today since A2S_INFO doesn't expose it.

## Teams

| Method | Path | Description |
|---|---|---|
| GET | `/teams` | Teams the current user belongs to |
| POST | `/teams` | Create a team (creator becomes Owner; also seeds Admin/Member roles) |

## Team Members & Invites

| Method | Path | Description |
|---|---|---|
| GET | `/teams/{teamId}/members` | Active members with role/status (any member) |
| PUT | `/teams/{teamId}/members/{userId}/role` | Change a member's role to Admin/Member (Owner only) |
| DELETE | `/teams/{teamId}/members/{userId}` | Remove a member (Owner), or leave the team (self) |
| GET | `/teams/{teamId}/invites` | Pending invites (any member) |
| POST | `/teams/{teamId}/invites` | Create an invite — body `{ inviteeDiscord? }`, returns a token, 7-day expiry |
| DELETE | `/teams/{teamId}/invites/{id}` | Revoke a pending invite |
| POST | `/team-invites/{token}/accept` | Accept an invite as the current user — top-level route since the accepter isn't a team member yet |

## Team Chat Templates

| Method | Path | Description |
|---|---|---|
| GET | `/teams/{teamId}/message-templates` | List templates for a team |
| POST | `/teams/{teamId}/message-templates` | Create — body `{ serverId?, eventType, templateText, isEnabled, cooldownSeconds }` (one per team+server+event) |
| PUT | `/teams/{teamId}/message-templates/{id}` | Update text/enabled/cooldown |
| DELETE | `/teams/{teamId}/message-templates/{id}` | Delete |
| GET | `/chat-templates/metadata` | Supported event types + placeholders (`{server} {grid} {time} {event} {player} {count} {team} {weapon}`) |
| POST | `/chat-templates/preview` | Body `{ templateText, eventType? }` → `{ rendered }`, using sample data |

No delivery yet — see the note in `docs/ARCHITECTURE.md#team-chat-automation-phase-5`.

## Map & Markers

| Method | Path | Description |
|---|---|---|
| GET | `/servers/{serverId}/map` | Map metadata for a server (auto-created on first access) |
| PUT | `/servers/{serverId}/map` | Update `imageUrl`/`width`/`height` |
| GET | `/servers/{serverId}/map/markers` | List markers |
| POST | `/servers/{serverId}/map/markers` | Create — body `{ type, x, y, label?, color?, isShared }` |
| PUT | `/servers/{serverId}/map/markers/{id}` | Update label/color/isShared |
| DELETE | `/servers/{serverId}/map/markers/{id}` | Delete |

## Analytics

| Method | Path | Description |
|---|---|---|
| GET | `/servers/{serverId}/analytics/summary?days=7` | Total raids, tier breakdown, raids-by-day, raids-by-hour (UTC), avg ping, avg/peak players — computed live, `days` clamped to 1-90 |

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
