import { useEffect, useRef } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { getDashboardConnection, stopDashboardConnection } from "@/lib/signalr";
import { useServers } from "./useServers";

/**
 * Connects to the DashboardHub once, subscribes to every server the user owns, and
 * invalidates the relevant React Query caches when the hub pushes an update. Falls back
 * gracefully to the existing polling intervals (useServers/useRecentRaidEvents) if the
 * WebSocket connection or a subscribe call fails — this is a latency improvement, not the
 * only way data reaches the UI.
 */
export function useDashboardRealtime() {
  const queryClient = useQueryClient();
  const { data: servers } = useServers();
  const serverIdsRef = useRef<string[]>([]);

  useEffect(() => {
    serverIdsRef.current = servers?.map((s) => s.id) ?? [];
  }, [servers]);

  useEffect(() => {
    const connection = getDashboardConnection();

    const subscribeToAllServers = async () => {
      for (const id of serverIdsRef.current) {
        try {
          await connection.invoke("Subscribe", id);
        } catch {
          // best-effort — a missed subscribe just means that server's pushes are delayed
        }
      }
    };

    const onServerStatusUpdated = () => {
      void queryClient.invalidateQueries({ queryKey: ["servers"] });
    };
    const onRaidEventCreated = () => {
      void queryClient.invalidateQueries({ queryKey: ["raid-events"] });
    };

    connection.on("ServerStatusUpdated", onServerStatusUpdated);
    connection.on("RaidEventCreated", onRaidEventCreated);
    connection.onreconnected(() => void subscribeToAllServers());

    if (connection.state === "Disconnected") {
      connection
        .start()
        .then(() => subscribeToAllServers())
        .catch(() => {
          // the dashboard still works via the existing polling intervals if this fails
        });
    }

    return () => {
      connection.off("ServerStatusUpdated", onServerStatusUpdated);
      connection.off("RaidEventCreated", onRaidEventCreated);
      void stopDashboardConnection();
    };
  }, [queryClient]);
}
