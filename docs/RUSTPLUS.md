# Rust+ Integration — Architecture, Confidence Levels, and What's Left

This documents the whole Rust+ subsystem honestly: what's verified, what's real but unverified,
and what's still missing. Read this before trusting or debugging any of it. The implementation
plan this is being built against lives at `C:\Users\barry\.claude\plans\zany-napping-seal.md`.

## What changed from the earlier version of this doc

The previous revision claimed the WebSocket client's protobuf schema was "verified" because
`protoc` compiled it. That was true but not the right bar — the schema compiled, but **every
single field number in `AppResponse` was wrong** (it used `2..11`, upstream is `4..13`), so no
response had ever actually decoded. `AppBroadcast` was `1/2/3` instead of `4/5/6`, so no broadcast
decoded either. `sellOrders` was field 12 (which is actually `outOfStock` upstream); field 13 —
the real `sellOrders` — was never read, so `/vending-machines` silently returned every marker
with an empty sell-order list. `playerToken` was `uint32`; real tokens are signed and negative
roughly half the time, so half of all valid tokens would have thrown `FormatException`. None of
this was caught earlier because nothing had a test that round-tripped an actual message.

The Stage B FCM auto-pairing stack described in the old doc (hand-rolled `checkin.proto`/
`mcs.proto`, `McsClient`, `RustPlusAutoPairingSession`) has been **deleted**. It was already
flagged as unverified, and it had a confirmed bug (sending a raw FCM token where Facepunch
actually expects an Expo push token). It's being replaced — see "Stage B, take two" below.

## 1. The WebSocket client — now verified against an independent reference

`server/src/Rustex.Infrastructure/RustPlus/RustPlusClient.cs` +
`server/src/Rustex.Infrastructure/RustPlus/Proto/rustplus.proto`.

The `.proto` schema is copied field-for-field from the community reference schema
([liamcottle/rustplus.js](https://github.com/liamcottle/rustplus.js/blob/master/rustplus.proto)),
not hand-reconstructed from memory. Two deliberate deviations: every `required` was changed to
`optional` (wire-identical in proto2, but degrades gracefully instead of throwing if Facepunch
ships a build that omits a field), and the clan/Nexus/camera-streaming messages were omitted
(out of scope for this app; protobuf silently skips unknown fields, so this doesn't break
decoding of messages that include them).

**This time it's actually verified, not just compiled:** `server/tests/Rustex.Api.Tests/RustPlus/ProtoFixtureTests.cs`
round-trips real byte fixtures generated with `protoc --encode` against the upstream schema
directly — independent of our generated C# code, so a bug in our `.proto` can't also be baked
into the fixture meant to catch it. All 7 tests pass: parsing a `teamInfo` response, parsing a
`mapMarkers` response with real sell orders, parsing a `teamMessage`/`entityChanged` broadcast,
and serializing requests (`getMapMarkers`, `setSubscription`, and a negative `playerToken`) to
the exact expected bytes.

**What this still doesn't prove:** that a live Rust+ server actually accepts this handshake and
responds the way the fixtures assume. The fixtures prove *our code speaks the schema correctly*;
they can't prove *the schema still matches whatever Facepunch is currently running*. That's the
one thing in this whole subsystem that genuinely needs a live server to confirm.

**Client hardening added alongside the schema fix:**
- A send lock (`ClientWebSocket.SendAsync` throws if two calls overlap, and this client is shared
  across concurrent HTTP requests — this was a real, reproducible bug, not a hypothetical one).
- A 60-second app-level heartbeat (`getTime`); two consecutive failures mark the connection
  faulted so pending requests fail immediately instead of hanging out their full 10s timeout.
- `IsHealthy` / `OnClosed`, and every pending request is faulted the moment the connection dies.

## 2. Session management — reconnects instead of staying dead

`server/src/Rustex.Infrastructure/RustPlus/RustPlusSession.cs` (new) +
`RustPlusConnectionManager.cs` (rewritten).

The old connection manager cached a `RustPlusClient` directly, so once its socket dropped —
server restart, wipe, idle timeout — every request against that pairing 502'd forever, with no
recovery path except deleting and re-saving the pairing. `RustPlusConnectionManager` now caches a
`RustPlusSession` instead: a supervisor loop that connects, sends `setSubscription` so broadcasts
actually start flowing (nothing previously enabled this), and reconnects with backoff
(1s → 60s, ±20% jitter) whenever the client dies. `RustPlusSessionWarmupWorker` opens a session
for every saved pairing on startup and every 5 minutes after, so background consumers (the
upcoming vending-alert poller, chat assistant) don't depend on an HTTP request having happened
first.

`RustPlusConnectionManager.OnBroadcast` is the fan-out point for live pushes — team position
changes, chat, entity state — tagged with the pairing id. Three independent Phase 6 workers each
subscribe to it directly (`RustPlusTeamTrackingWorker`, `RustPlusChatAssistantWorker`,
`RustPlusSmartDevicesWorker` for `entityChanged`), each with its own `Channel<>` so one slow
consumer can't back-pressure another.

## 3. Manual pairing — working today, unchanged in spirit

`RustPlusPairing` entity + `RustPlusController`. Store a token (encrypted at rest via
`IEncryptionService`), connect on demand, expose `GetTeamInfo`/vending-machine map markers as REST
endpoints. `POST pairing` now accepts the token as either its signed or unsigned 32-bit rendering
(`RustPlusTokenFormat.TryNormalize`, in `Rustex.Domain/RustPlus/`) — different community pairing
tools print it differently — and stores the canonical signed value. This is genuinely functional
for anyone who has a `(playerId, playerToken)` pair from any source; it does not depend on Stage B.

## 4. Stage B, take two — a local helper instead of a hand-rolled FCM stack

**The old auto-pairing approach was replaced, not just debugged**, because of a real architectural
constraint discovered while planning this: Facepunch's Rust+ login hands its token back via
`ReactNativeWebView.postMessage` — built for the mobile app's WebView, not a normal OAuth
redirect. **A plain website cannot capture it.** This is also why the closest competitor
(Rust On Top) ships as a Windows desktop app rather than a website.

**Built and verified against the real package** (reflection + its XML docs, not secondhand
claims — see `RustPlusApi.Fcm.Registration` v2.0.0-beta.6, pinned exactly since the 1.x stable
line targets net10.0 only): `tools/Rustex.PairingHelper` (`rustex-pair`) runs once on the user's
own machine, drives a real local Chrome via DevTools to capture the Steam login token — their
Steam password never touches the Rustex server — then uploads the resulting push credentials
(GCM identity + FCM/Expo tokens, never a password or session token) to Rustex.

Getting a token onto the helper without a plaintext credential in shell history: the signed-in
user generates a one-time code in the web UI (`POST /api/rustplus/link-codes`), types it into
`rustex-pair`, which redeems it (`POST link-codes/redeem`) for a 30-minute JWT on its own
audience/scheme — one that can authorize `PUT credentials` and *nothing else*, not even reading
the credentials back. Redemption raises an in-app notification so a leaked code doesn't silently
succeed unnoticed.

`POST /api/servers/{id}/rustplus/auto-pair` (the old per-request, Steam-auth-ticket, two-minute-
blocking version of this) now returns `410 Gone` pointing at the new setup.

## 5. The server-side listener

`RustPlusFcmListenerWorker` (a `BackgroundService`, gated on `RustPlus:EnableFcmListener`, off by
default) reconciles every 60s: one `RustPlusFcm` connection per user with `Active` credentials,
via a thin `IRustPlusFcmClient` wrapper (`RustPlusFcmClientAdapter`) that exists purely so the
worker can be pointed at a fake in tests instead of a live connection.

Events never touch `AppDbContext` from the socket thread that raises them — every handler goes
through a per-user `Channel<>`, drained by one consumer that opens its own DI scope per item.
`RustServerPairingHandler` (pulled out as its own class specifically so it's unit-testable against
an in-memory database — 6 tests, all passing) turns an `OnServerPairing` push into a created-or-
reused `RustServer` + upserted `RustPlusPairing`, exactly the same entities manual pairing writes
to. **Caveat inherited from the vendor package's own docs:** the pairing push's `Port` is the
Rust+ *companion* port, not the game port — there's no reliable way to derive the real game port
from it, so a newly auto-created server gets tagged `needs-review` rather than silently querying
the wrong port.

The other four event types (`EntityPairing`, `SmartSwitchPairing`, `SmartAlarmPairing`,
`StorageMonitorPairing`, `AlarmTriggered`) are republished through `RustPlusFcmEventBus` rather
than handled here — Smart Devices (Phase 6) subscribes to that bus once the entity to store them
in exists, without needing to modify this worker.

**Multi-instance safety:** before starting a user's session, the worker takes a Redis lock
(`TrySetIfAbsentAsync`, 90s TTL, renewed every 30s) — without it, two API replicas would both
listen and double-handle every push. This is the one piece of the whole subsystem that isn't
horizontally scalable by default; fine for a single-instance deployment, worth knowing about
before scaling out.

**Credential expiry (~14 days, a Steam/Facepunch limit, not a Rustex one):** there's no server-
side refresh — renewal needs the Chrome+Steam interaction, which by design only happens on the
user's own machine. The worker warns via notification 2 days out, then flips `Status` to
`NeedsReauth` and stops the session at expiry. This only affects *future* pairing/alarm pushes —
servers already paired keep working indefinitely regardless.

**What's genuinely unverified and can't be fixed by more code review:** whether Facepunch's Chrome-
based login flow still works today, whether the FCM/Expo/GCM endpoints behave the way the vendor
package assumes, and whether a live push actually arrives end-to-end. All of that needs one real
run against a live Steam account and Rust server to confirm — see the manual test procedure in
the plan document.

## 6. The five features

All built on the session/broadcast infrastructure above, and all functional with manual pairing
alone — none of them require the FCM auto-pairing stack from section 4, only a paired session.

**Team Tracking** (`RustPlusTeamTrackingWorker`) — syncs `RustPlusTeamMemberState` (unique per
`ServerId, SteamId`) from the `teamChanged` broadcast plus a 30s fallback poll (covers the gap
right after a reconnect, or a session where `setSubscription` silently failed). Transition
detection is a pure function, `Rustex.Domain.RustPlus.TeamStatusDetector` — death/revival takes
priority over online/offline when both flip in the same tick, so a player who dies and disconnects
in one update reports as a death, not a departure. Fires through `INotificationDispatcher`, not
through the existing `MessageTemplate`/`TemplateRenderer` in-game-chat pipeline — routing raid-style
`ChatEventTypes` through the team's configured chat templates for these would be the natural next
step, but doing that at the same time as building the roster/notification path risked confusing two
unrelated delivery mechanisms this late in the build. `GET team-state` serves the DB rows; `GET team`
still exists for a live round-trip.

**Vending Search** (`RustPlusVendingPollWorker`) — polls `getMapMarkers` for *every* actively
connected pairing (not just ones with an enabled Shop Alert — vending search needs fresh data on
its own), keeps `VendingMachineSnapshot`/`VendingListing` in sync via a full per-server resync each
tick, and computes `Rustex.Domain.RustPlus.VendingDiff` against the previous snapshot. `getMapMarkers`
includes a marker per online player, which can be a few hundred KB on a full server — the 60s
interval trades alert latency for bandwidth. `GET vending/search` reads only the DB.

**Shop Alerts** — `ShopAlert` entity + CRUD (`RustPlusShopAlertsController`), matched against
`VendingDiff` output by the same poll worker: kind (new listing / price drop / restock) must be
individually enabled on the alert, plus optional item id/name-contains/max-cost/min-stock filters
and a per-alert cooldown. `SoldOut`/`MachineDisappeared` diff kinds are deliberately not alertable —
no flag exists for them, since neither is actionable the way "it's now available" is.

**Smart Devices** (`RustPlusSmartDevicesWorker`) — populates `RustPlusSmartDevice` from FCM
entity-pairing pushes (`RustPlusFcmEventBus`) or manual entry (`RustPlusDevicesController`), and
keeps `LastKnownValue`/`LastKnownCapacity` in sync from `entityChanged` broadcasts. A Smart Alarm
going `value == true` — from either the live broadcast *or* the independent `OnAlarmTriggered` FCM
push, whichever arrives — raises a real `RaidEvent` with `Source = EventSourceKind.RustPlus`,
Tier 1, deduped per-server within a 10-second window across both paths. `AlarmNotification` (the FCM
push shape) carries no entity id, only a server id — so the dedupe key is per-server, not
per-device; a base with two alarms tripping within 10s of each other raises one event, which the
existing raid-alarm pipeline's own time-window clustering already treats as one incident anyway.
This supersedes the old `IRustPlusNotificationListener` stub, deleted in this phase.

**Chat Assistant** (`RustPlusChatAssistantWorker`) — ingests every `teamMessage` broadcast into
`RustPlusChatMessage` and answers `!help !pop !time !team !alerts !wipe !pos !device <name>` via
`Rustex.Domain.RustPlus.TeamChatCommandParser`, rate-limited per pairing (≥3s between replies,
≤20/min). The loop guard lives in the parser itself (`senderSteamId == botSteamId` → no match) —
both the worker's own auto-replies *and* messages sent from the web dashboard
(`POST .../rustplus/chat`) are recorded by whoever sent them, and the broadcast handler skips
messages from the pairing's own identity so a same-message echo (if Rust+ sends one back to the
sender) can't double-record it.

**Frontend** — `/rust-plus` (`RustPlusPage.tsx`): server selector, an account-level auto-pairing
setup card (`RustPlusAccountSetup`, one-time code generation), a per-server manual pairing form
when no pairing exists yet, and a tab per feature under `components/rustplus/`. `useRustPlusRealtime`
invalidates the relevant tab's query on the existing `NotificationCreated` SignalR push instead of
each tab needing its own hub subscription. Smoke-tested end-to-end against the real API + Postgres
(register → add server → manual pair → create a shop alert → all five tabs render) — not covered by
an automated test yet.

**Test coverage for this phase:** the pure decision logic is unit-tested (`TeamStatusDetector`,
`VendingDiff`, `TeamChatCommandParser`, `RustPlusTokenFormat`, `GridConverter`). The five new
workers themselves (`RustPlusTeamTrackingWorker`, `RustPlusVendingPollWorker`,
`RustPlusSmartDevicesWorker`, `RustPlusChatAssistantWorker`) are **not** — they're thin
orchestration over already-tested pure logic plus EF Core/`RustPlusConnectionManager` calls, and
testing them properly needs either a fake `RustPlusClient`/local test WebSocket or an EF InMemory
harness per worker, neither of which exists yet. `RustServerPairingHandler` (Phase 5) is the
existing example of the pattern to follow if/when that's built out.

## Where things are in the code

- `server/src/Rustex.Infrastructure/RustPlus/Proto/rustplus.proto` — corrected schema
- `server/src/Rustex.Infrastructure/RustPlus/RustPlusClient.cs` — hardened client
- `server/src/Rustex.Infrastructure/RustPlus/RustPlusSession.cs` + `RustPlusConnectionManager.cs` — reconnecting session management
- `server/src/Rustex.Infrastructure/RustPlus/RustPlusSessionWarmupWorker.cs` — startup/periodic warmup
- `server/src/Rustex.Domain/RustPlus/RustPlusTokenFormat.cs` — signed/unsigned token normalization
- `server/tests/Rustex.Api.Tests/RustPlus/ProtoFixtureTests.cs` — the golden-byte verification
- `server/src/Rustex.Api/Controllers/RustPlusAccountController.cs` + `RustPlusAccount.cs` entities — link-code/credential flow
- `server/src/Rustex.Infrastructure/RustPlus/Fcm/RustPlusFcmListenerWorker.cs` + `RustPlusFcmEventBus.cs` + `RustServerPairingHandler.cs` — the persistent listener
- `tools/Rustex.PairingHelper/` — the `rustex-pair` local helper (`dotnet tool` / standalone build)
- `server/src/Rustex.Infrastructure/RustPlus/RustPlusTeamTrackingWorker.cs` + `Rustex.Domain/RustPlus/TeamStatusTransition.cs` — Team Tracking
- `server/src/Rustex.Infrastructure/RustPlus/RustPlusVendingPollWorker.cs` + `Rustex.Domain/RustPlus/VendingDiff.cs` — Vending Search + Shop Alerts
- `server/src/Rustex.Api/Controllers/RustPlusShopAlertsController.cs` — Shop Alert CRUD
- `server/src/Rustex.Infrastructure/RustPlus/RustPlusSmartDevicesWorker.cs` + `server/src/Rustex.Api/Controllers/RustPlusDevicesController.cs` — Smart Devices
- `server/src/Rustex.Infrastructure/RustPlus/RustPlusChatAssistantWorker.cs` + `Rustex.Domain/RustPlus/TeamChatCommandParser.cs` — Chat Assistant
- `server/src/Rustex.Domain/Entities/RustPlusFeatures.cs` — the six Phase 6 entities (team member state, vending snapshots/listings, shop alerts, smart devices, chat messages)
- `server/src/Rustex.Domain/Abstractions/INotificationDispatcher.cs` + `Rustex.Infrastructure/Notifications/NotificationDispatcher.cs` — shared notification fan-out (in-app/SignalR, Discord, Web Push) used by all three Phase 6 workers that notify
- `client/src/pages/RustPlusPage.tsx` + `client/src/components/rustplus/` + `client/src/hooks/useRustPlus*.ts` — the frontend
