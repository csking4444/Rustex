import { motion } from "framer-motion";
import { Flame, ShieldOff } from "lucide-react";
import { Card, CardHeader } from "@/components/ui/Card";
import { SkeletonList } from "@/components/ui/Skeleton";
import { useRecentRaidEvents } from "@/hooks/useRaidEvents";
import type { RaidTier } from "@/types";

const TIER_BADGE: Record<RaidTier, string> = {
  Tier1: "badge-info",
  Tier2: "badge-warning",
  Tier3: "badge-critical",
};

const TIER_LABEL: Record<RaidTier, string> = {
  Tier1: "Tier 1",
  Tier2: "Tier 2",
  Tier3: "Tier 3",
};

export function RaidAlertFeed() {
  const { data: events, isLoading, isError } = useRecentRaidEvents(10);

  return (
    <Card>
      <CardHeader title="Raid Alerts" subtitle="Most recent detections across your servers" />

      {isLoading && <SkeletonList rows={4} />}

      {isError && <p className="text-sm text-critical">Couldn't load raid alerts. Retrying automatically.</p>}

      {!isLoading && !isError && events?.length === 0 && (
        <div className="flex flex-col items-center justify-center gap-2 py-8 text-text-muted">
          <ShieldOff className="h-8 w-8" />
          <p className="text-sm">No raids detected recently. Quiet out there.</p>
        </div>
      )}

      {!isLoading && events && events.length > 0 && (
        <ul className="flex flex-col gap-2">
          {events.map((event, i) => (
            <motion.li
              key={event.id}
              initial={{ opacity: 0, x: -8 }}
              animate={{ opacity: 1, x: 0 }}
              transition={{ delay: i * 0.03 }}
              className="flex items-center justify-between rounded-xl border border-white/5 bg-base-800/40 px-4 py-3"
            >
              <div className="flex items-center gap-3">
                <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-critical/15 text-critical">
                  <Flame className="h-4 w-4" />
                </div>
                <div>
                  <p className="text-sm font-medium text-text-primary">
                    {event.serverName}
                    {event.grid ? ` · Grid ${event.grid}` : ""}
                  </p>
                  <p className="text-xs text-text-muted">
                    {event.raidType ?? "unknown"} · {event.explosionCount} explosion{event.explosionCount === 1 ? "" : "s"} ·{" "}
                    {new Date(event.detectedAt).toLocaleTimeString()}
                  </p>
                </div>
              </div>
              <span className={TIER_BADGE[event.tier]}>{TIER_LABEL[event.tier]}</span>
            </motion.li>
          ))}
        </ul>
      )}
    </Card>
  );
}
