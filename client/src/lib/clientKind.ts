/**
 * "App" means the frontend is running installed/standalone (PWA) — the closest thing to a
 * native app this stack can detect without an actual native shell. Browsers can't register a
 * PWA with iOS/Android's telephony stack, so this is used to decide between a full-screen
 * ring-style alert (installed) and a plain desktop notification (regular browser tab) — see
 * EmergencyAlertDispatcher on the backend and RingAlertOverlay on the frontend.
 */
export function detectClientKind(): "app" | "desktop" {
  const isStandalone =
    window.matchMedia?.("(display-mode: standalone)").matches ||
    // iOS Safari's pre-standard flag for "added to home screen"
    (window.navigator as { standalone?: boolean }).standalone === true;

  return isStandalone ? "app" : "desktop";
}
