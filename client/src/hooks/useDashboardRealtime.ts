import { useCallback, useEffect, useRef, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { getDashboardConnection } from "@/lib/signalr";
import { useServers } from "./useServers";

/** One section of live state, as pushed by the server. */
interface LiveUpdate {
  scope: string;
  section: string;
  version: number;
  at: string;
  payload: unknown;
}

interface LiveSnapshot {
  scope: string;
  version: number;
  updatedAt: string;
  sections: Record<string, unknown>;
}

interface SubscribeResult {
  allowed: boolean;
  reason: string | null;
  snapshot: LiveSnapshot | null;
}

export type LiveStatus = "connecting" | "live" | "reconnecting" | "offline" | "denied";

/**
 * Keeps the dashboard synchronised with the server in real time.
 *
 * Three things make this survive a flaky connection rather than just being a fast path:
 *
 *  1. **Resume on (re)connect.** Subscribing returns the current snapshot in the same round trip,
 *     so a client that was disconnected is immediately correct instead of showing stale data
 *     until the next push — which for the 30s team poll could be half a minute of wrong info.
 *  2. **Gap detection.** Versions are monotonic per scope. If one arrives out of sequence we know
 *     a message was missed and re-fetch the whole snapshot rather than rendering state we cannot
 *     trust.
 *  3. **Graceful degradation.** Every failure path falls back to the existing polling intervals,
 *     so the dashboard is never blank because a WebSocket could not be established.
 */
export function useDashboardRealtime() {
  const queryClient = useQueryClient();
  const { data: servers } = useServers();
  const serverIdsRef = useRef<string[]>([]);

  const [status, setStatus] = useState<LiveStatus>("connecting");
  const [deniedReason, setDeniedReason] = useState<string | null>(null);

  // Last version seen per scope, used only for gap detection.
  const versionsRef = useRef<Map<string, number>>(new Map());

  useEffect(() => {
    serverIdsRef.current = servers?.map((s) => s.id) ?? [];
  }, [servers]);

  const applySnapshot = useCallback(
    (snapshot: LiveSnapshot | null) => {
      if (!snapshot) return;
      versionsRef.current.set(snapshot.scope, snapshot.version);

      // Sections map onto the caches that render them. Invalidating rather than writing the
      // payload straight in keeps one source of shape truth (the REST DTOs) instead of two.
      if ("status" in snapshot.sections) {
        void queryClient.invalidateQueries({ queryKey: ["servers"] });
      }
      if ("team" in snapshot.sections) {
        void queryClient.invalidateQueries({ queryKey: ["rustplus-team"] });
      }
      if ("devices" in snapshot.sections) {
        void queryClient.invalidateQueries({ queryKey: ["rustplus-devices"] });
      }
    },
    [queryClient],
  );

  useEffect(() => {
    const connection = getDashboardConnection();
    let cancelled = false;

    const subscribeAll = async () => {
      let anyAllowed = false;

      for (const id of serverIdsRef.current) {
        const scope = `server:${id}`;
        try {
          const result = await connection.invoke<SubscribeResult>("SubscribeScope", scope);

          if (!result?.allowed) {
            // Most often an expired plan. Surface it instead of silently showing a dashboard
            // that has quietly stopped updating.
            setDeniedReason(result?.reason ?? null);
            continue;
          }

          anyAllowed = true;
          applySnapshot(result.snapshot);
        } catch {
          // Best-effort — a missed subscribe just means that server falls back to polling.
        }
      }

      if (cancelled) return;
      if (anyAllowed) {
        setStatus("live");
        setDeniedReason(null);
      } else if (serverIdsRef.current.length > 0) {
        setStatus("denied");
      } else {
        setStatus("live");
      }
    };

    const onLiveUpdate = (update: LiveUpdate) => {
      const previous = versionsRef.current.get(update.scope);
      versionsRef.current.set(update.scope, update.version);

      // A version that is not exactly one ahead means we missed at least one message, so local
      // state is untrustworthy — pull the whole snapshot rather than applying a partial update.
      if (previous !== undefined && update.version !== previous + 1) {
        void connection
          .invoke<LiveSnapshot | null>("GetSnapshot", update.scope)
          .then(applySnapshot)
          .catch(() => {
            // Falls back to polling; nothing else to do here.
          });
        return;
      }

      if (update.section === "status") {
        void queryClient.invalidateQueries({ queryKey: ["servers"] });
      } else if (update.section === "team") {
        void queryClient.invalidateQueries({ queryKey: ["rustplus-team"] });
      } else if (update.section === "devices") {
        void queryClient.invalidateQueries({ queryKey: ["rustplus-devices"] });
      }
    };

    const onServerStatusUpdated = () => {
      void queryClient.invalidateQueries({ queryKey: ["servers"] });
    };
    const onRaidEventCreated = () => {
      void queryClient.invalidateQueries({ queryKey: ["raid-events"] });
    };

    const onReconnecting = () => setStatus("reconnecting");
    const onClose = () => setStatus("offline");

    connection.on("LiveUpdate", onLiveUpdate);
    connection.on("ServerStatusUpdated", onServerStatusUpdated);
    connection.on("RaidEventCreated", onRaidEventCreated);
    connection.onreconnecting(onReconnecting);
    connection.onclose(onClose);
    connection.onreconnected(() => {
      // Versions restart from whatever the server has now, so old ones would look like a gap.
      versionsRef.current.clear();
      void subscribeAll();
    });

    if (connection.state === "Disconnected") {
      setStatus("connecting");
      connection
        .start()
        .then(() => subscribeAll())
        .catch(() => {
          // The dashboard still works via the existing polling intervals if this fails.
          if (!cancelled) setStatus("offline");
        });
    } else if (connection.state === "Connected") {
      void subscribeAll();
    }

    return () => {
      cancelled = true;
      connection.off("LiveUpdate", onLiveUpdate);
      connection.off("ServerStatusUpdated", onServerStatusUpdated);
      connection.off("RaidEventCreated", onRaidEventCreated);
      // Deliberately not stopping the connection here — it's a shared singleton also used by
      // useEmergencyAlerts, and AppLayout owns its start/stop lifecycle (see there) so the two
      // consumers don't race to stop a connection the other is still using.
    };
  }, [queryClient, applySnapshot]);

  return { status, deniedReason };
}
