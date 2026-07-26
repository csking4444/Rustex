import { useEffect, useState } from "react";
import { Activity, BarChart3, Gauge, Siren, Users, type LucideIcon } from "lucide-react";
import { Card, CardHeader } from "@/components/ui/Card";
import { SkeletonList } from "@/components/ui/Skeleton";
import { BarChart } from "@/components/analytics/BarChart";
import { useServers } from "@/hooks/useServers";
import { useAnalyticsSummary } from "@/hooks/useAnalytics";

const DAY_OPTIONS = [7, 14, 30];

export default function AnalyticsPage() {
  const { data: servers, isLoading: serversLoading } = useServers();
  const [selectedServerId, setSelectedServerId] = useState<string | null>(null);
  const [days, setDays] = useState(7);

  useEffect(() => {
    if (!selectedServerId && servers && servers.length > 0) setSelectedServerId(servers[0].id);
  }, [servers, selectedServerId]);

  const { data: summary, isLoading } = useAnalyticsSummary(selectedServerId, days);

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-semibold text-text-primary">Analytics</h1>
          <p className="mt-1 text-sm text-text-muted">Computed live from raid and status history — no precomputed rollups yet.</p>
        </div>

        <div className="flex items-center gap-3">
          {servers && servers.length > 0 && (
            <select
              value={selectedServerId ?? ""}
              onChange={(e) => setSelectedServerId(e.target.value)}
              className="rounded-xl border border-white/10 bg-base-800/60 px-3 py-2 text-sm text-text-primary focus:outline-none focus:ring-2 focus:ring-blood-light/60"
            >
              {servers.map((s) => (
                <option key={s.id} value={s.id}>
                  {s.name}
                </option>
              ))}
            </select>
          )}
          <div className="flex overflow-hidden rounded-xl border border-white/10">
            {DAY_OPTIONS.map((d) => (
              <button
                key={d}
                onClick={() => setDays(d)}
                className={`px-3 py-2 text-xs font-medium transition-colors ${
                  days === d ? "bg-blood/20 text-white" : "bg-base-800/60 text-text-muted hover:text-white"
                }`}
              >
                {d}d
              </button>
            ))}
          </div>
        </div>
      </div>

      {!serversLoading && (!servers || servers.length === 0) && (
        <Card>
          <div className="flex flex-col items-center justify-center gap-2 py-12 text-text-muted">
            <BarChart3 className="h-8 w-8" />
            <p className="text-sm">Add a server first to see analytics for it.</p>
          </div>
        </Card>
      )}

      {selectedServerId && isLoading && <SkeletonList rows={4} />}

      {selectedServerId && summary && (
        <>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
            <StatTile icon={Siren} label={`Raids (${days}d)`} value={summary.totalRaids} />
            <StatTile icon={Activity} label="Tier 3 Raids" value={summary.tier3Count} accent="critical" />
            <StatTile icon={Gauge} label="Avg Ping" value={summary.avgPingMs !== null ? `${Math.round(summary.avgPingMs)}ms` : "—"} />
            <StatTile icon={Users} label="Peak Players" value={summary.peakPlayerCount ?? "—"} />
          </div>

          <div className="grid grid-cols-1 gap-6 xl:grid-cols-2">
            <Card>
              <CardHeader title="Raids per Day" />
              <BarChart data={summary.raidsByDay.map((d) => ({ label: d.date.slice(5), value: d.count }))} />
            </Card>

            <Card>
              <CardHeader title="Raids by Hour (UTC)" subtitle="Peak activity window" />
              <BarChart
                data={summary.raidsByHour.map((h) => ({ label: String(h.hourUtc), value: h.count }))}
                color="#4A7A96"
              />
            </Card>
          </div>

          <Card>
            <CardHeader title="Tier Breakdown" />
            <div className="flex gap-6">
              <TierBar label="Tier 1" count={summary.tier1Count} total={summary.totalRaids} color="#4A7A96" />
              <TierBar label="Tier 2" count={summary.tier2Count} total={summary.totalRaids} color="#D89A2B" />
              <TierBar label="Tier 3" count={summary.tier3Count} total={summary.totalRaids} color="#C1121F" />
            </div>
          </Card>
        </>
      )}
    </div>
  );
}

function StatTile({ icon: Icon, label, value, accent }: { icon: LucideIcon; label: string; value: string | number; accent?: "critical" }) {
  return (
    <div className="glass-panel flex items-center gap-4 p-5">
      <div
        className={`flex h-11 w-11 shrink-0 items-center justify-center rounded-xl border ${
          accent === "critical" ? "border-critical/30 bg-critical/15 text-critical" : "border-blood/30 bg-blood/15 text-blood-light"
        }`}
      >
        <Icon className="h-5 w-5" />
      </div>
      <div>
        <p className="text-xs text-text-muted">{label}</p>
        <p className="text-xl font-semibold text-text-primary">{value}</p>
      </div>
    </div>
  );
}

function TierBar({ label, count, total, color }: { label: string; count: number; total: number; color: string }) {
  const pct = total > 0 ? (count / total) * 100 : 0;
  return (
    <div className="flex-1">
      <div className="mb-1 flex items-center justify-between text-xs">
        <span className="text-text-secondary">{label}</span>
        <span className="text-text-muted">{count}</span>
      </div>
      <div className="h-2 overflow-hidden rounded-full bg-base-800">
        <div className="h-full rounded-full transition-all duration-300" style={{ width: `${pct}%`, backgroundColor: color }} />
      </div>
    </div>
  );
}
