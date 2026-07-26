import { useEffect, useState } from "react";
import { AnimatePresence, motion } from "framer-motion";
import { Outlet, useLocation } from "react-router-dom";
import { Sidebar } from "./Sidebar";
import { Topbar } from "./Topbar";
import { NotificationDrawer } from "./NotificationDrawer";
import { RingAlertOverlay } from "@/components/emergency/RingAlertOverlay";
import { useDashboardRealtime } from "@/hooks/useDashboardRealtime";
import { useEmergencyAlerts } from "@/hooks/useEmergencyAlerts";
import { stopDashboardConnection } from "@/lib/signalr";
import {
  useMarkAllNotificationsRead,
  useMarkNotificationRead,
  useNotifications,
  useUnreadNotificationCount,
} from "@/hooks/useNotifications";

export function AppLayout() {
  const [collapsed, setCollapsed] = useState(false);
  const [drawerOpen, setDrawerOpen] = useState(false);
  const location = useLocation();

  useDashboardRealtime();
  const { incomingCall, dismissIncomingCall } = useEmergencyAlerts();

  // Sole owner of the shared SignalR connection's teardown — useDashboardRealtime and
  // useEmergencyAlerts both attach listeners to it but don't stop it themselves, so two
  // independent effects can't race to stop a connection the other is still using. This only
  // fires when the authenticated app shell itself unmounts (e.g. logout), not on every re-render.
  useEffect(() => () => void stopDashboardConnection(), []);

  const { data: notifications } = useNotifications();
  const { data: unreadCount } = useUnreadNotificationCount();
  const markRead = useMarkNotificationRead();
  const markAllRead = useMarkAllNotificationsRead();

  return (
    <div className="flex h-screen w-screen overflow-hidden bg-base-950">
      <Sidebar collapsed={collapsed} onToggle={() => setCollapsed((c) => !c)} />

      <div className="flex min-w-0 flex-1 flex-col">
        <Topbar onOpenNotifications={() => setDrawerOpen(true)} notificationCount={unreadCount ?? 0} />

        <main className="flex-1 overflow-y-auto p-6">
          <AnimatePresence mode="wait">
            <motion.div
              key={location.pathname}
              initial={{ opacity: 0, y: 8 }}
              animate={{ opacity: 1, y: 0 }}
              exit={{ opacity: 0, y: -8 }}
              transition={{ duration: 0.18, ease: "easeOut" }}
            >
              <Outlet />
            </motion.div>
          </AnimatePresence>
        </main>
      </div>

      <NotificationDrawer
        isOpen={drawerOpen}
        onClose={() => setDrawerOpen(false)}
        notifications={notifications ?? []}
        onMarkRead={(id) => markRead.mutate(id)}
        onMarkAllRead={() => markAllRead.mutate()}
      />
      <RingAlertOverlay alert={incomingCall} onDismiss={dismissIncomingCall} />
    </div>
  );
}
