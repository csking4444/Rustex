# API Reference

Base URL (dev): `https://localhost:5443/api`

Only implemented endpoints are listed. Everything else in the original spec's API surface (PSTN call alerts, escalation) is scoped for later phases and will be documented here as it lands.

## Auth

| Method | Path | Description |
|---|---|---|
| POST | `/auth/register` | Body `{ email, username, password }` (password ≥8 chars) → token pair |
| POST | `/auth/login` | Body `{ email, password }` → token pair |
| GET | `/auth/discord/login` | Redirects to Discord OAuth2 authorize URL |
| GET | `/auth/discord/callback` | OAuth2 callback; exchanges code, upserts user, issues JWT + refresh token, redirects to frontend with tokens |
| GET | `/auth/google/login` | Redirects to Google OAuth2 consent screen |
| GET | `/auth/google/callback` | Callback; upserts user by Google subject, issues tokens |
| GET | `/auth/steam/login` | Redirects to Steam OpenID; callback creates-or-signs-in by SteamId64 (no auto-link to an existing email/password account — Steam gives no verifiable email) |
| POST | `/auth/steam/link/start` | `[Authorize]` — returns `{ url }` to link Steam to the *current* signed-in account (intent is pre-encoded into the OpenID nonce, since a top-level redirect can't carry a bearer header) |
| DELETE | `/auth/steam/link` | `[Authorize]` — unlink; refused if it's the account's only credential |
| GET | `/auth/steam/callback` | Shared callback for both login and link, branching on the nonce's `Purpose`. Replay-guarded (`TrySetIfAbsentAsync` on the nonce), validates `openid.signed` includes `op_endpoint,return_to,claimed_id,identity,response_nonce,assoc_handle`, `return_to` matches config, and the nonce timestamp is within ±5 min |
| POST | `/auth/refresh` | Body: `{ refreshToken }` → new access + refresh token pair (rotates the refresh token) |
| POST | `/auth/logout` | Revokes the current refresh token/session |
| GET | `/users/me` | Current authenticated user + profile (requires `Authorization: Bearer <token>`) |
| GET | `/users/me/settings` | Notification channel toggles + quiet hours (auto-created with defaults on first access) |
| PUT | `/users/me/settings` | Update — `quietHoursStart`/`quietHoursEnd` are `"HH:mm"` strings or null (both or neither) |

All three OAuth providers redirect back to the frontend with tokens in the URL fragment; `AuthCallbackPage.tsx` scrubs them via `history.replaceState` immediately after reading them.

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

## Rust+

Steam64 ids (`playerId`, `steamId`) are serialized as JSON **strings**, not numbers — they exceed
`Number.MAX_SAFE_INTEGER` and would silently lose precision otherwise (see
`Rustex.Api.Serialization.UlongStringConverter`, registered globally).

### Account-level setup (`/rustplus`)

| Method | Path | Description |
|---|---|---|
| POST | `/rustplus/link-codes` | `[Authorize]` — generates a one-time Crockford-base32 code (`RSTX-XXXX-XXXX`, 10-min TTL) for the `rustex-pair` local helper; retires any earlier unconsumed code |
| POST | `/rustplus/link-codes/redeem` | `[AllowAnonymous]` — body `{ code }` → a 30-minute JWT on a separate `Pairing` scheme that can *only* call `PUT credentials` |
| PUT | `/rustplus/credentials` | Scoped-JWT only — uploads FCM/GCM/Expo push credentials acquired by `rustex-pair` |
| GET | `/rustplus/credentials/status` | `[Authorize]` — `{ hasCredentials, status: Active\|NeedsReauth\|Disabled\|null, registeredAt, expiresAt, lastNotificationAt, steamId }`. Never returns the credentials themselves |
| DELETE | `/rustplus/credentials` | `[Authorize]` — deletes stored credentials, stops the listener session |

### Per-server (`/servers/{serverId}/rustplus`)

| Method | Path | Description |
|---|---|---|
| GET / POST / DELETE | `pairing` | Manual `(playerId, playerToken)` pairing — `playerToken` accepts either signed or unsigned 32-bit rendering (`RustPlusTokenFormat`) |
| GET | `team` | Live `GetTeamInfo` round-trip (blocks on the socket — prefer `team-state` below for UI) |
| GET | `team-state` | DB-backed roster kept fresh by `RustPlusTeamTrackingWorker` (teamChanged broadcast + 30s fallback poll) — includes `lastGrid`, `isOnline`, `isAlive` |
| GET | `vending-machines` | Live `GetMapMarkers` round-trip, filtered to vending machines |
| GET | `vending/search?q=&maxCost=&inStockOnly=` | DB-backed search over `VendingMachineSnapshot`/`VendingListing`, populated by `RustPlusVendingPollWorker` (60s poll) — never round-trips to the game server per keystroke |
| GET / POST / PUT / DELETE | `shop-alerts[/{id}]` | CRUD for `ShopAlert` — matched against `Rustex.Domain.RustPlus.VendingDiff` output by the same poll worker |
| GET / POST / PUT / DELETE | `devices[/{id}]` | CRUD for `RustPlusSmartDevice` (Switch/Alarm/StorageMonitor) — normally populated automatically by `RustPlusSmartDevicesWorker` from FCM entity-pairing pushes; POST covers manual entry |
| POST | `devices/{id}/value` | Body `{ value }` — toggles a Smart Switch live. Alarms/Storage Monitors are read-only in Rust+ itself |
| GET | `chat?limit=50` | Recent team chat, ingested by `RustPlusChatAssistantWorker` from the `teamMessage` broadcast |
| POST | `chat` | Body `{ message }` — sends into the game's team chat from the dashboard |
| POST | `auto-pair` | **410 Gone** — superseded by the link-code + `rustex-pair` flow above |

A paired Smart Alarm tripping (`entityChanged.value == true`, or the `AlarmTriggered` FCM push)
raises a real `RaidEvent` with `Source = RustPlus`, deduped per-server within a 10s window across
both signal paths — the only raid signal Rust+ can supply without a server plugin.

Team chat supports `!help !pop !time !team !alerts !wipe !pos !device <name>`, rate-limited to
one reply per 3s and 20/min per pairing.

### Reference data

| Method | Path | Description |
|---|---|---|
| GET | `/rust-items?q=&limit=20` | Public, no auth — item name/shortname search backing vending search and shop alert autocomplete |
| GET | `/rust-items/{itemId}` | Single item lookup |

See `docs/RUSTPLUS.md` for the full architecture, confidence levels, and what's still unverified.

## Health

| Method | Path | Description |
|---|---|---|
| GET | `/health` | Liveness/readiness (DB + Redis checks) |
| GET | `/version` | Build/phase info |

## Real-time — SignalR hub `/hubs/dashboard`

Connect with `?clientKind=app` (installed/standalone PWA) or omit it (plain browser tab) — see `client/src/lib/clientKind.ts`. Authenticated connections auto-join a per-user group in addition to whatever server groups they `Subscribe` to.

- Client → server: `Subscribe(serverId)`, `Unsubscribe(serverId)` — joins/leaves the `server:{id}` group
- Server → client, per server: `RaidEventCreated`, `ServerStatusUpdated`
- Server → client, per user (auto-joined on connect, no explicit subscribe needed): `IncomingRaidCall` (App-kind connections — full-screen ring alert), `RaidAlertNotification` (Desktop-kind connections — plain browser notification), `NotificationCreated` (fired by `INotificationDispatcher` for any non-emergency notification — Rust+ team status changes, shop alerts, device pairings; `{ id, type, title, body, severity, createdAt }`)

(Payload shapes in `Hubs/SignalRRaidEventBroadcaster.cs`.)

`EventIngestionWorker` (in `Rustex.Infrastructure`) consumes `SimulatedEventSource` for every registered server, clusters candidate events via `RaidAlarmEvaluator`, persists a `RaidEvent` + broadcasts `RaidEventCreated` for clusters reaching at least Tier 1, then hands off to `EmergencyAlertDispatcher` to notify the server owner. The event *source* is simulated; the clustering/tiering/dispatch logic downstream of it is real. Disable the simulator with `Ingestion__EnableSimulator=false`.

All authenticated endpoints expect `Authorization: Bearer <accessToken>`. Access tokens are short-lived (15 min default); use `/auth/refresh` to rotate.
