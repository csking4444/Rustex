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
changes, chat, entity state — tagged with the pairing id. Nothing subscribes to it yet; that
lands with Team Tracking / Chat Assistant / Smart Devices (Phase 6 of the plan).

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

The new design (not yet built — see the plan): a one-time local helper tool
(`rustex-pair`, using the maintained `RustPlusApi.Fcm.Registration` NuGet package, which drives a
real local Chrome via DevTools to intercept the token) runs once on the user's own machine — their
Steam password never touches the Rustex server. The resulting credentials upload to Rustex, which
then runs a persistent FCM listener server-side (`RustPlusApi.Fcm`) so that pressing "Pair With
Server" from the in-game Rust+ pause-menu tab registers the server automatically from then on.

`POST /api/servers/{id}/rustplus/auto-pair` (the old per-request, Steam-auth-ticket, two-minute-
blocking version of this) now returns `410 Gone` pointing at the new setup.

## Where things are in the code

- `server/src/Rustex.Infrastructure/RustPlus/Proto/rustplus.proto` — corrected schema
- `server/src/Rustex.Infrastructure/RustPlus/RustPlusClient.cs` — hardened client
- `server/src/Rustex.Infrastructure/RustPlus/RustPlusSession.cs` + `RustPlusConnectionManager.cs` — reconnecting session management
- `server/src/Rustex.Infrastructure/RustPlus/RustPlusSessionWarmupWorker.cs` — startup/periodic warmup
- `server/src/Rustex.Domain/RustPlus/RustPlusTokenFormat.cs` — signed/unsigned token normalization
- `server/tests/Rustex.Api.Tests/RustPlus/ProtoFixtureTests.cs` — the golden-byte verification
- `server/src/Rustex.Infrastructure/RustPlus/Fcm/` — currently just `RustPlusOptions.cs`; the
  local-helper tool and server-side listener land here next
