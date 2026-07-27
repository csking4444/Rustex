import { useEffect } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { getDashboardConnection } from "@/lib/signalr";

const TEAM_TYPES = new Set(["RustPlusTeamStatus"]);
const DEVICE_TYPES = new Set(["rustplus.device_paired"]);
const SHOP_ALERT_TYPES = new Set(["ShopAlert"]);

/**
 * Rust+ data mostly changes via background workers (team tracking, vending polling, smart
 * devices), which all go through INotificationDispatcher — so listening for the same
 * "NotificationCreated" push the notification center already uses is enough to invalidate the
 * relevant tab's query instead of waiting on its own poll interval. Modeled on
 * useDashboardRealtime: only ever calls connection.off in cleanup, never .stop() — the connection
 * is a shared singleton whose lifecycle AppLayout owns.
 */
export function useRustPlusRealtime(serverId: string | null) {
  const queryClient = useQueryClient();

  useEffect(() => {
    if (!serverId) return;

    const connection = getDashboardConnection();

    const onNotificationCreated = (payload: { type?: string }) => {
      if (!payload?.type) return;

      if (TEAM_TYPES.has(payload.type)) {
        void queryClient.invalidateQueries({ queryKey: ["rustplus", "team-state", serverId] });
      }
      if (DEVICE_TYPES.has(payload.type)) {
        void queryClient.invalidateQueries({ queryKey: ["rustplus", "devices", serverId] });
      }
      if (SHOP_ALERT_TYPES.has(payload.type)) {
        void queryClient.invalidateQueries({ queryKey: ["rustplus", "vending", "search", serverId] });
      }
    };

    connection.on("NotificationCreated", onNotificationCreated);

    return () => {
      connection.off("NotificationCreated", onNotificationCreated);
    };
  }, [queryClient, serverId]);
}
