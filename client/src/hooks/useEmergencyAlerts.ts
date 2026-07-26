import { useEffect, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { getDashboardConnection } from "@/lib/signalr";
import { ensureNotificationPermission, showDesktopNotification } from "@/lib/browserNotifications";
import type { EmergencyRaidAlertPayload, RaidTier } from "@/types";

const TIER_LABEL: Record<RaidTier, string> = { Tier1: "Tier 1", Tier2: "Tier 2", Tier3: "Tier 3" };

/**
 * Bridges the backend's two emergency-alert channels to the frontend:
 * - "IncomingRaidCall" (App-kind connections only) -> full-screen ring alert, handled by the
 *   caller via the returned `incomingCall` state (see RingAlertOverlay).
 * - "RaidAlertNotification" (Desktop-kind connections) -> a plain browser Notification.
 */
export function useEmergencyAlerts() {
  const [incomingCall, setIncomingCall] = useState<EmergencyRaidAlertPayload | null>(null);
  const queryClient = useQueryClient();

  useEffect(() => {
    void ensureNotificationPermission();

    const connection = getDashboardConnection();

    // Both channels correspond to a Notification row EmergencyAlertDispatcher just wrote —
    // refresh the notification center/badge either way.
    const refreshNotifications = () => void queryClient.invalidateQueries({ queryKey: ["notifications"] });

    const onIncomingRaidCall = (payload: EmergencyRaidAlertPayload) => {
      setIncomingCall(payload);
      refreshNotifications();
    };

    const onRaidAlertNotification = (payload: EmergencyRaidAlertPayload) => {
      showDesktopNotification(
        `${TIER_LABEL[payload.tier]} raid — ${payload.serverName}`,
        payload.grid
          ? `Grid ${payload.grid} · ${payload.explosionCount} explosion${payload.explosionCount === 1 ? "" : "s"}`
          : `${payload.explosionCount} explosion${payload.explosionCount === 1 ? "" : "s"}`,
      );
      refreshNotifications();
    };

    connection.on("IncomingRaidCall", onIncomingRaidCall);
    connection.on("RaidAlertNotification", onRaidAlertNotification);

    return () => {
      connection.off("IncomingRaidCall", onIncomingRaidCall);
      connection.off("RaidAlertNotification", onRaidAlertNotification);
    };
  }, [queryClient]);

  return { incomingCall, dismissIncomingCall: () => setIncomingCall(null) };
}
