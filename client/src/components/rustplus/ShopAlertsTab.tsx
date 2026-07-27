import { useState, type FormEvent } from "react";
import { Megaphone, Trash2 } from "lucide-react";
import { Card, CardHeader } from "@/components/ui/Card";
import { SkeletonList } from "@/components/ui/Skeleton";
import {
  useCreateShopAlert,
  useDeleteShopAlert,
  useRustPlusShopAlerts,
  useUpdateShopAlert,
} from "@/hooks/useRustPlusShopAlerts";

export function ShopAlertsTab({ serverId }: { serverId: string }) {
  const { data: alerts, isLoading } = useRustPlusShopAlerts(serverId);
  const createAlert = useCreateShopAlert(serverId);
  const updateAlert = useUpdateShopAlert(serverId);
  const deleteAlert = useDeleteShopAlert(serverId);

  const [itemNameContains, setItemNameContains] = useState("");
  const [maxCost, setMaxCost] = useState("");

  function handleSubmit(e: FormEvent) {
    e.preventDefault();
    if (!itemNameContains.trim()) return;
    createAlert.mutate(
      {
        itemId: null,
        itemNameContains: itemNameContains.trim(),
        maxCostPerItem: maxCost ? Number(maxCost) : null,
        minAmountInStock: 1,
        notifyOnNewListing: true,
        notifyOnPriceDrop: true,
        notifyOnRestock: true,
        cooldownSeconds: 900,
      },
      { onSuccess: () => setItemNameContains("") },
    );
  }

  return (
    <Card>
      <CardHeader title="Shop Alerts" subtitle="Notified when a matching listing appears, drops in price, or restocks." />

      <form onSubmit={handleSubmit} className="mb-4 flex flex-wrap items-end gap-3">
        <label className="flex flex-1 min-w-[160px] flex-col gap-1">
          <span className="text-xs text-text-muted">Item name contains</span>
          <input
            value={itemNameContains}
            onChange={(e) => setItemNameContains(e.target.value)}
            placeholder="e.g. AK"
            className="rounded-xl border border-white/10 bg-base-800/60 px-3 py-2 text-sm text-text-primary focus:outline-none focus:ring-2 focus:ring-blood-light/60"
          />
        </label>
        <label className="flex w-32 flex-col gap-1">
          <span className="text-xs text-text-muted">Max cost</span>
          <input
            type="number"
            value={maxCost}
            onChange={(e) => setMaxCost(e.target.value)}
            placeholder="Any"
            className="rounded-xl border border-white/10 bg-base-800/60 px-3 py-2 text-sm text-text-primary focus:outline-none focus:ring-2 focus:ring-blood-light/60"
          />
        </label>
        <button type="submit" disabled={createAlert.isPending} className="btn-primary">
          <Megaphone className="h-4 w-4" />
          Add Alert
        </button>
      </form>

      {isLoading && <SkeletonList rows={3} />}

      {!isLoading && (!alerts || alerts.length === 0) && (
        <div className="flex flex-col items-center justify-center gap-2 py-8 text-text-muted">
          <Megaphone className="h-8 w-8" />
          <p className="text-sm">No shop alerts yet.</p>
        </div>
      )}

      {!isLoading && alerts && alerts.length > 0 && (
        <ul className="flex flex-col gap-2">
          {alerts.map((a) => (
            <li
              key={a.id}
              className="flex items-center justify-between rounded-xl border border-white/5 bg-base-800/40 px-4 py-3"
            >
              <div>
                <p className="text-sm font-medium text-text-primary">{a.itemName ?? a.itemNameContains ?? "Any item"}</p>
                <p className="text-xs text-text-muted">
                  {a.maxCostPerItem ? `≤${a.maxCostPerItem} scrap` : "Any price"} ·{" "}
                  {a.lastTriggeredAt ? `last fired ${new Date(a.lastTriggeredAt).toLocaleString()}` : "never fired"}
                </p>
              </div>
              <div className="flex items-center gap-2">
                <label className="flex items-center gap-1.5 text-xs text-text-muted">
                  <input
                    type="checkbox"
                    checked={a.isEnabled}
                    onChange={(e) =>
                      updateAlert.mutate({
                        id: a.id,
                        itemId: a.itemId,
                        itemNameContains: a.itemNameContains,
                        maxCostPerItem: a.maxCostPerItem,
                        minAmountInStock: a.minAmountInStock,
                        notifyOnNewListing: a.notifyOnNewListing,
                        notifyOnPriceDrop: a.notifyOnPriceDrop,
                        notifyOnRestock: a.notifyOnRestock,
                        isEnabled: e.target.checked,
                        cooldownSeconds: a.cooldownSeconds,
                      })
                    }
                    className="h-4 w-4 rounded border-white/20 bg-base-800 accent-blood"
                  />
                  Enabled
                </label>
                <button
                  type="button"
                  onClick={() => deleteAlert.mutate(a.id)}
                  className="rounded-lg p-2 text-text-muted transition-colors hover:bg-critical/10 hover:text-critical"
                >
                  <Trash2 className="h-4 w-4" />
                </button>
              </div>
            </li>
          ))}
        </ul>
      )}
    </Card>
  );
}
