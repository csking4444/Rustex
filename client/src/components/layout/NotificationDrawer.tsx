import { AnimatePresence, motion } from "framer-motion";
import { BellOff, X } from "lucide-react";

export interface DrawerNotification {
  id: string;
  title: string;
  body?: string;
  severity: "info" | "warning" | "critical";
  createdAt: string;
}

interface NotificationDrawerProps {
  isOpen: boolean;
  onClose: () => void;
  notifications: DrawerNotification[];
}

const SEVERITY_BADGE: Record<DrawerNotification["severity"], string> = {
  info: "badge-info",
  warning: "badge-warning",
  critical: "badge-critical",
};

export function NotificationDrawer({ isOpen, onClose, notifications }: NotificationDrawerProps) {
  return (
    <AnimatePresence>
      {isOpen && (
        <>
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            onClick={onClose}
            className="fixed inset-0 z-40 bg-black/50"
          />
          <motion.div
            initial={{ x: "100%" }}
            animate={{ x: 0 }}
            exit={{ x: "100%" }}
            transition={{ type: "tween", duration: 0.25, ease: "easeOut" }}
            className="fixed right-0 top-0 z-50 flex h-screen w-full max-w-sm flex-col border-l border-white/5 bg-base-900 shadow-panel"
          >
            <div className="flex h-16 shrink-0 items-center justify-between border-b border-white/5 px-5">
              <h2 className="text-sm font-semibold text-text-primary">Notifications</h2>
              <button onClick={onClose} className="text-text-muted hover:text-white">
                <X className="h-4 w-4" />
              </button>
            </div>

            <div className="flex-1 overflow-y-auto p-4">
              {notifications.length === 0 ? (
                <div className="flex h-full flex-col items-center justify-center gap-2 text-text-muted">
                  <BellOff className="h-8 w-8" />
                  <p className="text-sm">You're all caught up.</p>
                </div>
              ) : (
                <ul className="flex flex-col gap-2">
                  {notifications.map((n) => (
                    <li key={n.id} className="glass-panel glass-panel-hover p-3">
                      <div className="flex items-start justify-between gap-2">
                        <p className="text-sm font-medium text-text-primary">{n.title}</p>
                        <span className={SEVERITY_BADGE[n.severity]}>{n.severity}</span>
                      </div>
                      {n.body && <p className="mt-1 text-xs text-text-muted">{n.body}</p>}
                      <p className="mt-2 text-[11px] text-text-muted">{new Date(n.createdAt).toLocaleString()}</p>
                    </li>
                  ))}
                </ul>
              )}
            </div>
          </motion.div>
        </>
      )}
    </AnimatePresence>
  );
}
