# Rust+ Integration — Architecture, Confidence Levels, and What's Left

This documents the whole Rust+ subsystem honestly: what's real and verified, what's real but
unverified, and what's an intentional stub. Read this before trusting or debugging any of it.

## The three layers

### 1. `RustPlusClient` (Stage A) — ✅ verified

`server/src/Rustex.Infrastructure/RustPlus/RustPlusClient.cs` + `Proto/rustplus.proto`.

This connects to a paired Rust server's WebSocket and speaks the `AppRequest`/`AppMessage`
protobuf protocol (GetTeamInfo, GetMapMarkers, GetEntityInfo, SendTeamMessage, etc.). The
`.proto` schema is a reconstruction from community reverse-engineering (this protocol has been
publicly documented and stable for years via projects like liamcottle/rustplus.js), but unlike
everything else in this document, **it was verified**: `protoc` (via the `Grpc.Tools` MSBuild
integration) successfully compiled it into working C# message classes, and the surrounding
client code builds against those real generated types. That doesn't guarantee every field number
matches the live server exactly, but the structure is sound and it's the highest-confidence part
of this whole subsystem.

**Requires:** a `(playerId, playerToken)` pair and the server's IP/query-app-port. Manual entry
today via `POST /api/servers/{id}/rustplus/pairing` — see `docs/API.md`.

### 2. Manual pairing (Stage A, the usable-today path) — ✅ working

`RustPlusPairing` entity + `RustPlusController` + `RustPlusConnectionManager`. Store a token
(encrypted at rest), connect on demand, expose `GetTeamInfo`/vending-machine map markers as
REST endpoints. This is genuinely functional for anyone who has a `(playerId, playerToken)` pair
from any source (a community pairing tool, or manually inspecting their own paired app's
traffic) — it does not depend on Stage 3 below at all.

### 3. FCM auto-pairing (Stage B) — ⚠️ experimental, unverified, needs debugging

**This is the part to be skeptical of.** Automatically obtaining a pairing token (so a user
never has to manually enter one — just presses "Pair with Rust+" in-game) requires impersonating
the Rust+ mobile app's Firebase Cloud Messaging registration:

1. **Android device check-in** (`https://android.clients.google.com/checkin`) — register a fake
   Android device, get back an `androidId` + `securityToken`. Uses Google's `checkin.proto`.
2. **GCM/FCM registration** (`https://android.clients.google.com/c2dm/register3`) — register for
   push notifications against Rust+'s specific Firebase sender ID, get back a registration token.
3. **Register that token with Facepunch** — a REST call to Facepunch's companion-rust API tying
   the push token to the user's Steam ID, so their servers know where to push pairing events.
4. **MCS (Mobile Connection Server)** — a persistent TLS connection to `mtalk.google.com:5228`
   using Google's `mcs.proto` binary framing, listening for incoming push payloads.
5. Parse the pairing payload (server IP/port, playerId, playerToken) out of the push message and
   save it as a `RustPlusPairing` row automatically.

**Confidence breakdown:**
- Steps 1-3 are plain HTTP/REST calls. The *shape* of these calls is documented by community
  projects (e.g. `MatthieuLemoine/push-receiver`, a Node.js reference implementation), and I've
  implemented them following that shape as closely as I could recall — but I have not verified
  the exact request fields against a live call, and Google/Facepunch could have changed
  something since my training data.
- Step 4's wire *framing* (a version byte, then repeated `[tag byte][varint length][protobuf
  payload]` frames) is a simple, stable, mechanical structure I'm confident about.
- Step 4's *message schema* (the exact field numbers inside `LoginRequest`, `DataMessage`, etc.)
  is the single least-verified piece of this entire project. Getting this wrong doesn't crash
  loudly — it just means the connection silently never receives a usable pairing push.

**If this doesn't work when you test it:** that's expected, not a sign something is badly wrong.
The fix path is comparing this implementation's protobuf field numbers against a known-working
reference (e.g. reading `push-receiver`'s `mcs.proto` file directly) rather than re-guessing from
scratch. I could not do that comparison myself while writing this, since I don't have live
internet access to fetch that reference during this session.

## Where things are in the code

- `server/src/Rustex.Infrastructure/RustPlus/Fcm/` — checkin, registration, and MCS client
- `server/src/Rustex.Infrastructure/RustPlus/RustPlusAutoPairingService.cs` — orchestrates the
  above into a background service, disabled by default (`RustPlus:EnableAutoPairing`)
