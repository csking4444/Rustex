// Service worker — installability requirement (fetch handler, see clientKind.ts) plus a real
// Web Push handler so raid alerts reach a backgrounded or fully closed PWA. Does not cache
// anything for offline use yet; that's a separate concern from push delivery.

self.addEventListener("install", () => {
  self.skipWaiting();
});

self.addEventListener("activate", (event) => {
  event.waitUntil(self.clients.claim());
});

self.addEventListener("fetch", () => {
  // Intentionally a no-op passthrough — required for installability on some browsers.
});

self.addEventListener("push", (event) => {
  let data = { title: "Rustex", body: "A raid alert came in." };
  try {
    if (event.data) data = event.data.json();
  } catch {
    // fall back to the default above if the payload isn't JSON
  }

  event.waitUntil(
    self.registration.showNotification(data.title ?? "Rustex", {
      body: data.body ?? "",
      tag: "rustex-raid-alert",
      requireInteraction: true,
      data: { notificationId: data.notificationId ?? null },
    }),
  );
});

self.addEventListener("notificationclick", (event) => {
  event.notification.close();
  event.waitUntil(
    self.clients.matchAll({ type: "window", includeUncontrolled: true }).then((clientList) => {
      for (const client of clientList) {
        if ("focus" in client) return client.focus();
      }
      if (self.clients.openWindow) return self.clients.openWindow("/dashboard");
    }),
  );
});
