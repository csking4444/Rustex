import { apiClient } from "./apiClient";

function urlBase64ToUint8Array(base64String: string): Uint8Array {
  const padding = "=".repeat((4 - (base64String.length % 4)) % 4);
  const base64 = (base64String + padding).replace(/-/g, "+").replace(/_/g, "/");
  const rawData = window.atob(base64);
  const outputArray = new Uint8Array(rawData.length);
  for (let i = 0; i < rawData.length; i++) outputArray[i] = rawData.charCodeAt(i);
  return outputArray;
}

/** Subscribes this browser to Web Push and registers it with the backend. Returns false (not
 * an error) if push isn't supported or the server has no VAPID key configured — it's an
 * optional channel, not a hard requirement. */
export async function subscribeToPush(): Promise<boolean> {
  if (!("serviceWorker" in navigator) || !("PushManager" in window)) return false;

  const { data } = await apiClient.get<{ publicKey: string | null }>("/push/vapid-public-key");
  if (!data.publicKey) return false;

  const registration = await navigator.serviceWorker.ready;

  let subscription = await registration.pushManager.getSubscription();
  if (!subscription) {
    subscription = await registration.pushManager.subscribe({
      userVisibleOnly: true,
      // TS's TypedArray types became generic over the buffer type in newer @types/dom, which
      // makes Uint8Array's inferred type not structurally match BufferSource here even though
      // it is one at runtime — a plain cast is the standard workaround.
      applicationServerKey: urlBase64ToUint8Array(data.publicKey) as BufferSource,
    });
  }

  const json = subscription.toJSON();
  if (!json.endpoint || !json.keys?.p256dh || !json.keys?.auth) return false;

  await apiClient.post("/push/subscriptions", {
    endpoint: json.endpoint,
    p256dhKey: json.keys.p256dh,
    authKey: json.keys.auth,
  });

  return true;
}

export async function unsubscribeFromPush(): Promise<void> {
  if (!("serviceWorker" in navigator)) return;
  const registration = await navigator.serviceWorker.ready;
  const subscription = await registration.pushManager.getSubscription();
  if (!subscription) return;

  await apiClient.post("/push/unsubscribe", { endpoint: subscription.endpoint });
  await subscription.unsubscribe();
}
