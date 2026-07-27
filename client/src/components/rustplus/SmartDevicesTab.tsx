import { Cpu, Power, Siren, Warehouse } from "lucide-react";
import { Card, CardHeader } from "@/components/ui/Card";
import { SkeletonList } from "@/components/ui/Skeleton";
import { useRustPlusDevices, useSetRustPlusDeviceValue, useUpdateRustPlusDevice } from "@/hooks/useRustPlusDevices";
import type { RustPlusSmartDevice } from "@/types";

const DEVICE_ICON: Record<RustPlusSmartDevice["type"], typeof Power> = {
  Switch: Power,
  Alarm: Siren,
  StorageMonitor: Warehouse,
};

export function SmartDevicesTab({ serverId }: { serverId: string }) {
  const { data: devices, isLoading } = useRustPlusDevices(serverId);
  const setValue = useSetRustPlusDeviceValue(serverId);
  const updateDevice = useUpdateRustPlusDevice(serverId);

  return (
    <Card>
      <CardHeader title="Smart Devices" subtitle="Populated automatically once you pair a Smart Switch, Alarm, or Storage Monitor in-game." />

      {isLoading && <SkeletonList rows={3} />}

      {!isLoading && (!devices || devices.length === 0) && (
        <div className="flex flex-col items-center justify-center gap-2 py-12 text-text-muted">
          <Cpu className="h-8 w-8" />
          <p className="text-sm">No smart devices paired yet.</p>
        </div>
      )}

      {!isLoading && devices && devices.length > 0 && (
        <ul className="flex flex-col gap-2">
          {devices.map((d) => {
            const Icon = DEVICE_ICON[d.type];
            return (
              <li
                key={d.id}
                className="flex items-center justify-between rounded-xl border border-white/5 bg-base-800/40 px-4 py-3"
              >
                <div className="flex items-center gap-3">
                  <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-white/5 text-text-secondary">
                    <Icon className="h-4 w-4" />
                  </div>
                  <div>
                    <p className="text-sm font-medium text-text-primary">{d.name}</p>
                    <p className="text-xs text-text-muted">
                      {d.type}
                      {d.lastKnownValue !== null && ` · ${d.lastKnownValue ? "on" : "off"}`}
                      {d.lastChangedAt && ` · updated ${new Date(d.lastChangedAt).toLocaleTimeString()}`}
                    </p>
                  </div>
                </div>

                {d.type === "Switch" && (
                  <button
                    type="button"
                    onClick={() => setValue.mutate({ id: d.id, value: !d.lastKnownValue })}
                    className={d.lastKnownValue ? "badge-success" : "badge"}
                  >
                    {d.lastKnownValue ? "On" : "Off"}
                  </button>
                )}

                {d.type === "Alarm" && (
                  <label className="flex items-center gap-1.5 text-xs text-text-muted">
                    <input
                      type="checkbox"
                      checked={d.alarmRaisesRaidEvent}
                      onChange={(e) => updateDevice.mutate({ id: d.id, name: d.name, alarmRaisesRaidEvent: e.target.checked })}
                      className="h-4 w-4 rounded border-white/20 bg-base-800 accent-blood"
                    />
                    Raid alert
                  </label>
                )}
              </li>
            );
          })}
        </ul>
      )}
    </Card>
  );
}
