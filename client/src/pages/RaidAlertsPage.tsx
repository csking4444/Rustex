import { useEffect, useState, type ChangeEvent } from "react";
import { Flame, Save, ShieldOff, Siren } from "lucide-react";
import { Card, CardHeader } from "@/components/ui/Card";
import { SkeletonList } from "@/components/ui/Skeleton";
import { useServers } from "@/hooks/useServers";
import { useRecentRaidEvents } from "@/hooks/useRaidEvents";
import { useRaidAlarmSettings, useUpdateRaidAlarmSettings } from "@/hooks/useRaidAlarmSettings";
import type { RaidTier } from "@/types";

const TIER_BADGE: Record<RaidTier, string> = {
  Tier1: "badge-info",
  Tier2: "badge-warning",
  Tier3: "badge-critical",
};
const TIER_LABEL: Record<RaidTier, string> = { Tier1: "Tier 1", Tier2: "Tier 2", Tier3: "Tier 3" };

export default function RaidAlertsPage() {
  const { data: servers, isLoading: serversLoading } = useServers();
  const [selectedServerId, setSelectedServerId] = useState<string | null>(null);

  useEffect(() => {
    if (!selectedServerId && servers && servers.length > 0) {
      setSelectedServerId(servers[0].id);
    }
  }, [servers, selectedServerId]);

  const { data: events, isLoading: eventsLoading } = useRecentRaidEvents(50);
  const serverEvents = events?.filter((e) => e.serverId === selectedServerId) ?? [];

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-semibold text-text-primary">Raid Alerts</h1>
          <p className="mt-1 text-sm text-text-muted">
            Tiers are count-based: 1+ explosions in a cluster is Tier 1, 3+ is Tier 2, 5+ is Tier 3 by default —
            adjust the thresholds per server below.
          </p>
        </div>

        {servers && servers.length > 0 && (
          <select
            value={selectedServerId ?? ""}
            onChange={(e) => setSelectedServerId(e.target.value)}
            className="rounded-xl border border-white/10 bg-base-800/60 px-3 py-2 text-sm text-text-primary focus:outline-none focus:ring-2 focus:ring-blood-light/60"
          >
            {servers.map((s) => (
              <option key={s.id} value={s.id}>
                {s.name}
              </option>
            ))}
          </select>
        )}
      </div>

      {!serversLoading && (!servers || servers.length === 0) && (
        <Card>
          <div className="flex flex-col items-center justify-center gap-2 py-12 text-text-muted">
            <Siren className="h-8 w-8" />
            <p className="text-sm">Add a server first to configure raid alarms for it.</p>
          </div>
        </Card>
      )}

      {selectedServerId && (
        <div className="grid grid-cols-1 gap-6 xl:grid-cols-2">
          <RaidAlarmSettingsForm serverId={selectedServerId} />

          <Card>
            <CardHeader title="Recent Detections" subtitle="For the selected server" />

            {eventsLoading && <SkeletonList rows={4} />}

            {!eventsLoading && serverEvents.length === 0 && (
              <div className="flex flex-col items-center justify-center gap-2 py-8 text-text-muted">
                <ShieldOff className="h-8 w-8" />
                <p className="text-sm">No raids detected recently on this server.</p>
              </div>
            )}

            {!eventsLoading && serverEvents.length > 0 && (
              <ul className="flex flex-col gap-2">
                {serverEvents.map((event) => (
                  <li
                    key={event.id}
                    className="flex items-center justify-between rounded-xl border border-white/5 bg-base-800/40 px-4 py-3"
                  >
                    <div className="flex items-center gap-3">
                      <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-critical/15 text-critical">
                        <Flame className="h-4 w-4" />
                      </div>
                      <div>
                        <p className="text-sm font-medium text-text-primary">{event.grid ? `Grid ${event.grid}` : "Unknown grid"}</p>
                        <p className="text-xs text-text-muted">
                          {event.raidType ?? "unknown"} · {event.explosionCount} explosion{event.explosionCount === 1 ? "" : "s"} ·{" "}
                          {new Date(event.detectedAt).toLocaleTimeString()}
                        </p>
                      </div>
                    </div>
                    <span className={TIER_BADGE[event.tier]}>{TIER_LABEL[event.tier]}</span>
                  </li>
                ))}
              </ul>
            )}
          </Card>
        </div>
      )}
    </div>
  );
}

function RaidAlarmSettingsForm({ serverId }: { serverId: string }) {
  const { data: settings, isLoading } = useRaidAlarmSettings(serverId);
  const updateSettings = useUpdateRaidAlarmSettings(serverId);

  const [form, setForm] = useState({
    isEnabled: true,
    tier1Threshold: 1,
    tier2Threshold: 3,
    tier3Threshold: 5,
    timeWindowSeconds: 30,
    clusterRadius: 50,
    cooldownSeconds: 120,
  });

  useEffect(() => {
    if (settings) {
      setForm({
        isEnabled: settings.isEnabled,
        tier1Threshold: settings.tier1Threshold,
        tier2Threshold: settings.tier2Threshold,
        tier3Threshold: settings.tier3Threshold,
        timeWindowSeconds: settings.timeWindowSeconds,
        clusterRadius: settings.clusterRadius,
        cooldownSeconds: settings.cooldownSeconds,
      });
    }
  }, [settings]);

  function field(key: keyof typeof form) {
    return {
      value: form[key] as number,
      onChange: (e: ChangeEvent<HTMLInputElement>) =>
        setForm((f) => ({ ...f, [key]: Number(e.target.value) })),
    };
  }

  return (
    <Card>
      <CardHeader
        title="Alarm Settings"
        subtitle={isLoading ? undefined : "Explosion-count thresholds and clustering window"}
        action={
          <label className="flex items-center gap-2 text-xs text-text-muted">
            <input
              type="checkbox"
              checked={form.isEnabled}
              onChange={(e) => setForm((f) => ({ ...f, isEnabled: e.target.checked }))}
              className="h-4 w-4 rounded border-white/20 bg-base-800 accent-blood"
            />
            Enabled
          </label>
        }
      />

      {isLoading && <SkeletonList rows={4} />}

      {!isLoading && (
        <form
          onSubmit={(e) => {
            e.preventDefault();
            updateSettings.mutate(form);
          }}
          className="flex flex-col gap-4"
        >
          <div className="grid grid-cols-3 gap-3">
            <LabeledInput label="Tier 1 at" suffix="explosions" {...field("tier1Threshold")} />
            <LabeledInput label="Tier 2 at" suffix="explosions" {...field("tier2Threshold")} />
            <LabeledInput label="Tier 3 at" suffix="explosions" {...field("tier3Threshold")} />
          </div>

          <div className="grid grid-cols-3 gap-3">
            <LabeledInput label="Time window" suffix="seconds" {...field("timeWindowSeconds")} />
            <LabeledInput label="Cluster radius" suffix="units" {...field("clusterRadius")} />
            <LabeledInput label="Cooldown" suffix="seconds" {...field("cooldownSeconds")} />
          </div>

          <button type="submit" disabled={updateSettings.isPending} className="btn-primary self-start">
            <Save className="h-4 w-4" />
            {updateSettings.isPending ? "Saving..." : "Save Settings"}
          </button>

          {updateSettings.isError && (
            <p className="text-xs text-critical">
              Couldn't save — thresholds must be positive and non-decreasing (Tier 1 ≤ Tier 2 ≤ Tier 3).
            </p>
          )}
        </form>
      )}
    </Card>
  );
}

function LabeledInput({
  label,
  suffix,
  value,
  onChange,
}: {
  label: string;
  suffix: string;
  value: number;
  onChange: (e: ChangeEvent<HTMLInputElement>) => void;
}) {
  return (
    <label className="flex flex-col gap-1">
      <span className="text-xs text-text-muted">{label}</span>
      <div className="flex items-center gap-2">
        <input
          type="number"
          min={0}
          value={value}
          onChange={onChange}
          className="w-full rounded-xl border border-white/10 bg-base-800/60 px-3 py-2 text-sm text-text-primary focus:outline-none focus:ring-2 focus:ring-blood-light/60"
        />
        <span className="whitespace-nowrap text-xs text-text-muted">{suffix}</span>
      </div>
    </label>
  );
}
