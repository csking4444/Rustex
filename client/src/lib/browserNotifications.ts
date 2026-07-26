export async function ensureNotificationPermission(): Promise<boolean> {
  if (!("Notification" in window)) return false;
  if (Notification.permission === "granted") return true;
  if (Notification.permission === "denied") return false;

  const result = await Notification.requestPermission();
  return result === "granted";
}

export function showDesktopNotification(title: string, body: string): void {
  if (!("Notification" in window) || Notification.permission !== "granted") return;

  const notification = new Notification(title, {
    body,
    // icon: "/icons/icon-192.png", // add real branded icon assets under client/public/icons before enabling
    tag: "rustex-raid-alert",
  });

  notification.onclick = () => {
    window.focus();
    notification.close();
  };
}
