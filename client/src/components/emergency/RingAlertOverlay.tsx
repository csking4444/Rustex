import { useEffect } from "react";
import { AnimatePresence, motion } from "framer-motion";
import { PhoneIncoming, PhoneOff } from "lucide-react";
import { startSiren, stopSiren } from "@/lib/siren";
import type { EmergencyRaidAlertPayload, RaidTier } from "@/types";

const TIER_LABEL: Record<RaidTier, string> = { Tier1: "Tier 1", Tier2: "Tier 2", Tier3: "Tier 3" };

interface RingAlertOverlayProps {
  alert: EmergencyRaidAlertPayload | null;
  onDismiss: () => void;
}

/**
 * The closest thing to a real "incoming call" a PWA can do: full-screen, looping siren,
 * device vibration where supported. Not a real VOIP/telephony call — a browser has no way to
 * register with iOS/Android's native call stack, so this can't ring through silent mode or
 * show up as a system call UI the way a native app with CallKit/ConnectionService could. See
 * docs/ARCHITECTURE.md for the honest breakdown of what would be needed for that.
 */
export function RingAlertOverlay({ alert, onDismiss }: RingAlertOverlayProps) {
  useEffect(() => {
    if (!alert) return;

    startSiren();
    if ("vibrate" in navigator) navigator.vibrate([400, 200, 400, 200, 400]);

    return () => stopSiren();
  }, [alert]);

  return (
    <AnimatePresence>
      {alert && (
        <motion.div
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          className="fixed inset-0 z-[100] flex flex-col items-center justify-center gap-8 bg-base-950/97 px-6 text-center backdrop-blur-sm"
        >
          <motion.div
            animate={{ scale: [1, 1.08, 1] }}
            transition={{ duration: 1, repeat: Infinity, ease: "easeInOut" }}
            className="flex h-24 w-24 items-center justify-center rounded-full border-2 border-critical bg-critical/20 shadow-glow-critical"
          >
            <PhoneIncoming className="h-10 w-10 text-critical" />
          </motion.div>

          <div>
            <p className="text-xs uppercase tracking-widest text-critical">{TIER_LABEL[alert.tier]} raid detected</p>
            <h2 className="mt-2 text-2xl font-semibold text-text-primary">{alert.serverName}</h2>
            <p className="mt-1 text-sm text-text-secondary">
              {alert.grid ? `Grid ${alert.grid} · ` : ""}
              {alert.explosionCount} explosion{alert.explosionCount === 1 ? "" : "s"}
              {alert.raidType ? ` · ${alert.raidType}` : ""}
            </p>
          </div>

          <button onClick={onDismiss} className="btn-primary gap-2 px-8 py-3 text-base">
            <PhoneOff className="h-5 w-5" />
            Acknowledge
          </button>
        </motion.div>
      )}
    </AnimatePresence>
  );
}
