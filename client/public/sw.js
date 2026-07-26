// Minimal service worker — its only job right now is to exist, since several browsers require
// one (with a fetch handler) as part of their PWA installability criteria, which is what makes
// `display-mode: standalone` detection meaningful (see src/lib/clientKind.ts). It does not cache
// anything yet; that's a Phase 5+ concern once the app actually needs to work offline, and the
// same place a real Push API handler (for alerts while the app is fully closed, not just
// backgrounded) would eventually go — see docs/ARCHITECTURE.md.

self.addEventListener("install", () => {
  self.skipWaiting();
});

self.addEventListener("activate", (event) => {
  event.waitUntil(self.clients.claim());
});

self.addEventListener("fetch", () => {
  // Intentionally a no-op passthrough — required for installability on some browsers.
});
