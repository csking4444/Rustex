# Roadmap

Status legend: ✅ done · 🚧 in progress · ⬜ planned

## Phase 1 — Foundation ✅ (this repo)

- ✅ Repo scaffold, Docker Compose, CI skeleton
- ✅ Full domain schema (Postgres reference SQL + EF entity model)
- ✅ Discord OAuth + JWT + refresh tokens + role/permission tables
- ✅ Tactical theme, app shell (sidebar/topbar/notification drawer)
- ✅ Dashboard skeleton over stub/simulated data
- ✅ SignalR hub scaffold

## Phase 2 — Server Management 🚧

- ✅ Server CRUD (name, IP, ports, map, seed, world size, tags, favorites)
- ✅ Live ping/status polling — `A2sQueryClient` speaks the Source engine A2S_INFO protocol directly to each server's query port (real data, no plugin/Rust+ needed); `ServerStatusPollingWorker` polls every 20s, persists `ServerStatusSnapshot` rows, and pushes `ServerStatusUpdated` over SignalR
- ✅ Player count tracking (via A2S_INFO)
- ⬜ Queue size (A2S_INFO doesn't expose it — needs EDF keyword parsing or a plugin bridge)
- ⬜ Redis caching of last-known state (currently reads the latest DB snapshot per request)
- ⬜ Wipe & restart schedule automation (fields exist, no scheduling logic yet)
- ⬜ Server grouping and multi-server dashboard aggregation beyond the current flat list

## Phase 3 — Raid Alarm System 🚧

- ✅ **Tier-based classification, not subjective severity.** `RaidTier` is `Tier1`/`Tier2`/`Tier3`, driven purely by how many qualifying events land in one cluster. Defaults: 1+ = Tier 1, 3+ = Tier 2, 5+ = Tier 3 — all three thresholds are per-server settings, not hardcoded (`RaidAlarmSettings`, editable via `/api/servers/{id}/raid-alarm-settings` and the Raid Alerts page).
- ✅ `RaidAlarmEvaluator` (`Rustex.Domain.RaidAlarm`) — framework-free clustering by time window + radius, unit-testable without a database.
- ✅ `EventIngestionWorker` rewritten around the evaluator: real streaming clustering (not a fixed debounce), per-server cooldown, settings loaded per server (falling back to defaults if unconfigured).
- ⬜ False-positive filtering (PvE, NPCs, Bradley, Patrol Helicopter, friendly events) — no such event types exist in `SimulatedEventSource` yet, so there's nothing to filter out today; the settings shape has room for it once a richer source exists.
- ⬜ `PluginWebhookEventSource` — real explosion-level ingestion via an original Oxide/Carbon companion plugin (separate sub-project, MIT-licensed, published independently)
- ✅ Raid alert cards (server, time, grid, count, tier, raid type, estimated size) — map location awaits Phase 6's map

## Phase 4 — Notifications & Emergency Alert System 🚧

**Design pivot:** the original spec described PSTN phone calls via Twilio/Vonage/Plivo. That's still schema'd (`PhoneNumber`, `CallAlertSetting`, `CallAlert`, `IVoiceCallProvider`) for later, but the primary emergency channel is now **platform-aware in-app alerting**, since it's buildable and testable today without third-party call credentials:

- ✅ **Trigger source, in addition to explosions:** `IRustPlusNotificationListener` — the contract for listening to Rust+ Smart Alarm push notifications (the one real raid signal Rust+ itself provides, via FCM, when an in-game Smart Alarm is tripped). Implementation is a documented stub (`RustPlusNotificationListener`) — the FCM/pairing handshake is undocumented by Facepunch and needs a live account to build against; see its doc comment for exactly what's missing. Once wired up, a Smart Alarm ping becomes a `RaidCandidateEvent` and feeds the *same* `RaidAlarmEvaluator` pipeline as everything else — "amount of notifications" driving tier is literally this.
- ✅ **Delivery, platform-aware:** `EmergencyAlertDispatcher` picks the channel per user based on their live connection's `ClientKind`:
  - **App** (installed/standalone PWA — detected via `display-mode: standalone`): a full-screen `RingAlertOverlay` with a synthesized looping siren (Web Audio API, no external asset) and device vibration where supported.
  - **Desktop** (regular browser tab): a plain browser `Notification`.
  - ⚠️ **Honest limitation:** neither of these is a real VOIP/telephony call. A browser or PWA cannot register with iOS/Android's native call stack (CallKit/ConnectionService) — that requires a native app shell, which this repo doesn't build. The ring alert is the closest achievable approximation: loud, full-screen, hard to miss, but it won't ring through silent mode or show a system call UI. Revisit if/when a native wrapper (e.g. Capacitor) is added.
- ✅ Per-user, per-server (or global) `CallAlertSetting` — min tier, cooldown, enable/disable — resolved by the dispatcher, defaulting to "alert on any Tier 1+" if unconfigured.
- ⬜ Web Push for App-kind users who are fully backgrounded/closed, not just live-connected (SignalR only reaches open connections) — service worker (`client/public/sw.js`) exists but has no push handler yet.
- ⬜ Twilio/Vonage/Plivo PSTN calling as an additional opt-in channel (schema ready, `IVoiceCallProvider` implementations not started)
- ⬜ Escalation state machine (retry → secondary contact → Discord → push), call history UI
- ⬜ Quiet hours, smart filtering (min explosion count/duration, ignore PvE/Bradley/heli) beyond what `RaidAlarmSettings` already covers at the detection layer

## Phase 5 — Rust Team Chat Automation 🚧

- ✅ Template editor with all 8 placeholders (`{server}`, `{grid}`, `{time}`, `{event}`, `{player}`, `{count}`, `{team}`, `{weapon}`) — `TemplateRenderer` (`Rustex.Domain.Templating`) is pure substitution logic, reused by both the preview endpoint and (once wired) real sending
- ✅ Full event type catalog from the spec (`ChatEventTypes`), one template per team+server+event (or team-wide with `serverId: null`)
- ✅ Preview endpoint (`POST /api/chat-templates/preview`) renders against sample data so a template can be checked without a real event
- ✅ Per-template cooldown + enable/disable, managed from the Teams page
- ⬜ **Delivery is still nothing** — posting into actual in-game team chat depends on the same Rust+ pairing or plugin bridge as Phase 3/4's other gaps. The template CRUD/preview system is fully real; nothing consumes a template to send a message yet, since there's no bridge to send it through.

## Phase 6 — Interactive Map ✅ (with an honest substitution for "the map")

- ✅ Marker CRUD (`RaidMarker`/`TeamMarker`/`PlayerMarker`/`MonumentMarker`/`CustomMarker`, per-server, lazily-created `MapData` row) — `/api/servers/{id}/map` + `/map/markers`
- ✅ Custom pan/zoom coordinate-plane viewer (`InteractiveMap`, native SVG `viewBox` + `getScreenCTM()` for exact click/drag math) — click-to-place, click-to-inspect/delete, grid overlay
- ⬜ **Not MapLibre, not a real Rust map image.** There's no public tile source for Rust's procedurally-generated terrain (each seed is unique, Facepunch exposes no map imagery API). `MapData.imageUrl` exists for a server-supplied image (e.g. from a service like RustMaps.com) to be layered in behind the grid later — swapping that in is additive, not a rewrite.
- ⬜ Heatmap layer over historical raid data, monument/cargo/heli auto-markers (needs the same event-detail source as Phase 3's plugin bridge)

## Phase 7 — Team Features 🚧

- ✅ Team invites (token-based, 7-day expiry, copy-link flow, accept endpoint) and member list
- ✅ Three system roles per team (Owner/Admin/Member) created at team creation; Owner can change other members' roles and remove members, any member can leave
- ✅ Remove/leave, role change API + UI
- ⬜ Permission catalog (`Permission`/`team_role_permissions` tables exist) isn't seeded or enforced anywhere yet — authorization today is just "Owner can do X, any member can do Y", not a real permission matrix
- ⬜ Shared markers/alerts/notes beyond what Phase 6's map already shares (`isShared` on `Marker`), activity log, member status (online/offline/sleeping/down — needs a live in-game presence source, same gap as everything else needing a plugin bridge)

## Phase 8 — Analytics ✅ (on-demand, not precomputed)

- ✅ `GET /api/servers/{id}/analytics/summary?days=7|14|30` — total raids, tier breakdown, raids-by-day, raids-by-hour (UTC), avg ping, avg/peak player count, computed live from `RaidEvents`/`ServerStatusSnapshots`
- ✅ Charts on the Analytics page (custom lightweight bar charts, no charting library dependency)
- ⬜ **Deliberately not using `AnalyticsSnapshot`/background aggregation jobs yet** — simple `Count`/`Average` SQL aggregates are reliably translatable and always fresh; a precomputed rollup is worth adding once raid volume makes scanning raw tables per request expensive, not before
- ⬜ Alert trends, cross-server comparison, export

## Phase 9 — Optimization ⬜

- Load testing, query/index tuning, background worker scaling
- Redis caching pass on hot read endpoints (deliberately not added speculatively this round — see Known constraints)

## Phase 10 — Production Deployment ✅ (baseline) / ⬜ (hardening)

- ✅ Docker Compose (dev) + `docker-compose.prod.yml` overlay with an Nginx TLS-terminating edge proxy, GitHub Actions CI (build/lint/test/Docker image sanity build)
- ⬜ Actual deploy workflow (CI builds and tests; nothing pushes images or deploys anywhere yet), observability (structured log shipping, metrics/tracing), real TLS certs / domain

---

## Known constraints carried across phases

- **Rust+ only gives limited telemetry.** Full raid detection depends on Phase 3's plugin bridge, not Rust+ alone. This was an explicit scope decision — see [ARCHITECTURE.md](ARCHITECTURE.md#event-ingestion).
- **Team chat automation** similarly depends on a bridge back into the game (plugin or Rust+ device pairing) — the templates/automation UI can be fully built before that bridge exists, but won't deliver messages in-game until it does.
