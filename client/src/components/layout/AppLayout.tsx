import { useState } from "react";
import { AnimatePresence, motion } from "framer-motion";
import { Outlet, useLocation } from "react-router-dom";
import { Sidebar } from "./Sidebar";
import { Topbar } from "./Topbar";
import { NotificationDrawer } from "./NotificationDrawer";
import { RingAlertOverlay } from "@/components/emergency/RingAlertOverlay";
import { useDashboardRealtime } from "@/hooks/useDashboardRealtime";
import { useEmergencyAlerts } from "@/hooks/useEmergencyAlerts";

export function AppLayout() {
  const [collapsed, setCollapsed] = useState(false);
  const [drawerOpen, setDrawerOpen] = useState(false);
  const location = useLocation();

  useDashboardRealtime();
  const { incomingCall, dismissIncomingCall } = useEmergencyAlerts();

  return (
    <div className="flex h-screen w-screen overflow-hidden bg-base-950">
      <Sidebar collapsed={collapsed} onToggle={() => setCollapsed((c) => !c)} />

      <div className="flex min-w-0 flex-1 flex-col">
        <Topbar onOpenNotifications={() => setDrawerOpen(true)} notificationCount={0} />

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

      <NotificationDrawer isOpen={drawerOpen} onClose={() => setDrawerOpen(false)} notifications={[]} />
      <RingAlertOverlay alert={incomingCall} onDismiss={dismissIncomingCall} />
    </div>
  );
}
