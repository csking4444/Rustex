import { useEffect, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { Bell, Link2, Moon, Save } from "lucide-react";
import { Card, CardHeader } from "@/components/ui/Card";
import { SkeletonList } from "@/components/ui/Skeleton";
import { useAuth } from "@/context/AuthContext";
import { useUpdateUserSettings, useUserSettings } from "@/hooks/useUserSettings";
import { subscribeToPush, unsubscribeFromPush } from "@/lib/webPush";

const CHANNEL_TOGGLES: { key: ToggleKey; label: string; description: string }[] = [
  { key: "soundEnabled", label: "Sound", description: "Play a sound alongside in-app alerts" },
  { key: "desktopEnabled", label: "Desktop Notifications", description: "Browser notifications on this device" },
  { key: "browserEnabled", label: "Browser Tab Alerts", description: "Ring/notification overlay while a tab is open" },
  { key: "discordEnabled", label: "Discord Webhooks", description: "Post alerts to configured Discord webhooks" },
  { key: "pushEnabled", label: "Push Notifications", description: "Reach this device even when the app is closed (requires server VAPID keys)" },
  { key: "callEnabled", label: "Emergency Ring Alerts", description: "Full-screen ring alert on installed/standalone app" },
];

type ToggleKey = "soundEnabled" | "desktopEnabled" | "browserEnabled" | "discordEnabled" | "pushEnabled" | "callEnabled";

const TIMEZONES = ["UTC", "America/New_York", "America/Chicago", "America/Denver", "America/Los_Angeles", "Europe/London", "Europe/Berlin", "Australia/Sydney"];

export default function SettingsPage() {
  const { data: settings, isLoading } = useUserSettings();
  const updateSettings = useUpdateUserSettings();
  const { user, linkSteam, unlinkSteam } = useAuth();
  const [searchParams] = useSearchParams();
  const linked = searchParams.get("linked");
  const [steamActionError, setSteamActionError] = useState<string | null>(null);
  const [unlinkingSteam, setUnlinkingSteam] = useState(false);

  async function handleUnlinkSteam() {
    setSteamActionError(null);
    setUnlinkingSteam(true);
    try {
      await unlinkSteam();
    } catch (err) {
      const message =
        (err as { response?: { data?: string } })?.response?.data ?? "Couldn't unlink Steam — try again.";
      setSteamActionError(typeof message === "string" ? message : "Couldn't unlink Steam — try again.");
    } finally {
      setUnlinkingSteam(false);
    }
  }

  const [form, setForm] = useState({
    soundEnabled: true,
    desktopEnabled: true,
    browserEnabled: true,
    discordEnabled: false,
    pushEnabled: false,
    callEnabled: false,
    quietHoursStart: "",
    quietHoursEnd: "",
    quietHoursTimezone: "UTC",
  });
  const [quietHoursOn, setQuietHoursOn] = useState(false);

  useEffect(() => {
    if (settings) {
      setForm({
        soundEnabled: settings.soundEnabled,
        desktopEnabled: settings.desktopEnabled,
        browserEnabled: settings.browserEnabled,
        discordEnabled: settings.discordEnabled,
        pushEnabled: settings.pushEnabled,
        callEnabled: settings.callEnabled,
        quietHoursStart: settings.quietHoursStart ?? "22:00",
        quietHoursEnd: settings.quietHoursEnd ?? "08:00",
        quietHoursTimezone: settings.quietHoursTimezone,
      });
      setQuietHoursOn(settings.quietHoursStart !== null);
    }
  }, [settings]);

  const [pushWarning, setPushWarning] = useState<string | null>(null);

  function toggle(key: ToggleKey) {
    setForm((f) => ({ ...f, [key]: !f[key] }));
  }

  async function handleSave() {
    setPushWarning(null);

    if (form.pushEnabled) {
      const subscribed = await subscribeToPush().catch(() => false);
      if (!subscribed) {
        setPushWarning("Couldn't enable push — either this browser doesn't support it or the server has no VAPID key configured.");
      }
    } else {
      await unsubscribeFromPush().catch(() => {});
    }

    updateSettings.mutate({
      ...form,
      quietHoursStart: quietHoursOn ? form.quietHoursStart : null,
      quietHoursEnd: quietHoursOn ? form.quietHoursEnd : null,
    });
  }

  return (
    <div className="flex flex-col gap-6">
      <div>
        <h1 className="text-2xl font-semibold text-text-primary">Settings</h1>
        <p className="mt-1 text-sm text-text-muted">Notification channels and quiet hours. Theme, raid detection sensitivity live on the Raid Alerts page (per server).</p>
      </div>

      {isLoading && <SkeletonList rows={5} />}

      {!isLoading && (
        <>
          {linked === "steam" && (
            <p className="rounded-xl border border-success/30 bg-success/10 px-4 py-2 text-sm text-success">
              Steam account linked.
            </p>
          )}

          <Card>
            <CardHeader title="Connected Accounts" subtitle="Sign-in methods linked to this account" />
            <div className="flex items-center justify-between py-1">
              <div className="flex items-start gap-3">
                <Link2 className="mt-0.5 h-4 w-4 shrink-0 text-text-muted" />
                <div>
                  <p className="text-sm font-medium text-text-primary">Steam</p>
                  <p className="text-xs text-text-muted">
                    {user?.hasSteam ? "Linked — used to receive Rust+ pairing pushes too." : "Not linked."}
                  </p>
                </div>
              </div>
              {user?.hasSteam ? (
                <button onClick={() => void handleUnlinkSteam()} disabled={unlinkingSteam} className="btn-secondary">
                  {unlinkingSteam ? "Unlinking..." : "Unlink"}
                </button>
              ) : (
                <div className="flex flex-col items-end gap-1">
                  <button onClick={() => void linkSteam()} className="btn-secondary">
                    Link Steam
                  </button>
                  <button
                    onClick={() => void linkSteam(true)}
                    className="text-xs text-text-muted underline-offset-2 hover:text-white hover:underline"
                  >
                    Pick a different account
                  </button>
                </div>
              )}
            </div>
            {steamActionError && <p className="mt-2 text-xs text-warning">{steamActionError}</p>}
          </Card>

          <Card>
            <CardHeader title="Notification Channels" subtitle="Which channels deliver raid alerts" />
            <div className="flex flex-col divide-y divide-white/5">
              {CHANNEL_TOGGLES.map(({ key, label, description }) => (
                <div key={key} className="flex items-center justify-between py-3 first:pt-0 last:pb-0">
                  <div className="flex items-start gap-3">
                    <Bell className="mt-0.5 h-4 w-4 shrink-0 text-text-muted" />
                    <div>
                      <p className="text-sm font-medium text-text-primary">{label}</p>
                      <p className="text-xs text-text-muted">{description}</p>
                    </div>
                  </div>
                  <button
                    role="switch"
                    aria-checked={form[key]}
                    onClick={() => toggle(key)}
                    className={`relative h-6 w-11 shrink-0 rounded-full transition-colors ${form[key] ? "bg-blood" : "bg-base-700"}`}
                  >
                    <span
                      className={`absolute top-0.5 h-5 w-5 rounded-full bg-white transition-transform ${
                        form[key] ? "translate-x-5" : "translate-x-0.5"
                      }`}
                    />
                  </button>
                </div>
              ))}
            </div>
          </Card>

          <Card>
            <CardHeader
              title="Quiet Hours"
              subtitle="Suppress ring alerts during this window — desktop notifications still show"
              action={
                <button
                  role="switch"
                  aria-checked={quietHoursOn}
                  onClick={() => setQuietHoursOn((v) => !v)}
                  className={`relative h-6 w-11 rounded-full transition-colors ${quietHoursOn ? "bg-blood" : "bg-base-700"}`}
                >
                  <span
                    className={`absolute top-0.5 h-5 w-5 rounded-full bg-white transition-transform ${
                      quietHoursOn ? "translate-x-5" : "translate-x-0.5"
                    }`}
                  />
                </button>
              }
            />

            {quietHoursOn && (
              <div className="flex flex-wrap items-end gap-4">
                <label className="flex flex-col gap-1">
                  <span className="flex items-center gap-1.5 text-xs text-text-muted">
                    <Moon className="h-3.5 w-3.5" /> From
                  </span>
                  <input
                    type="time"
                    value={form.quietHoursStart}
                    onChange={(e) => setForm((f) => ({ ...f, quietHoursStart: e.target.value }))}
                    className="rounded-xl border border-white/10 bg-base-800/60 px-3 py-2 text-sm text-text-primary focus:outline-none focus:ring-2 focus:ring-blood-light/60"
                  />
                </label>
                <label className="flex flex-col gap-1">
                  <span className="text-xs text-text-muted">To</span>
                  <input
                    type="time"
                    value={form.quietHoursEnd}
                    onChange={(e) => setForm((f) => ({ ...f, quietHoursEnd: e.target.value }))}
                    className="rounded-xl border border-white/10 bg-base-800/60 px-3 py-2 text-sm text-text-primary focus:outline-none focus:ring-2 focus:ring-blood-light/60"
                  />
                </label>
                <label className="flex flex-col gap-1">
                  <span className="text-xs text-text-muted">Timezone</span>
                  <select
                    value={form.quietHoursTimezone}
                    onChange={(e) => setForm((f) => ({ ...f, quietHoursTimezone: e.target.value }))}
                    className="rounded-xl border border-white/10 bg-base-800/60 px-3 py-2 text-sm text-text-primary focus:outline-none focus:ring-2 focus:ring-blood-light/60"
                  >
                    {TIMEZONES.map((tz) => (
                      <option key={tz} value={tz}>
                        {tz}
                      </option>
                    ))}
                  </select>
                </label>
              </div>
            )}
          </Card>

          <div className="flex flex-col items-start gap-2">
            <button onClick={() => void handleSave()} disabled={updateSettings.isPending} className="btn-primary">
              <Save className="h-4 w-4" />
              {updateSettings.isPending ? "Saving..." : "Save Settings"}
            </button>
            {pushWarning && <p className="text-xs text-warning">{pushWarning}</p>}
          </div>
        </>
      )}
    </div>
  );
}
