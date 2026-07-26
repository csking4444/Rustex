import { StatTiles } from "@/components/dashboard/StatTiles";
import { ServerStatusGrid } from "@/components/dashboard/ServerStatusGrid";
import { RaidAlertFeed } from "@/components/dashboard/RaidAlertFeed";

export default function DashboardPage() {
  return (
    <div className="flex flex-col gap-6">
      <div>
        <h1 className="text-2xl font-semibold text-text-primary">Dashboard</h1>
        <p className="mt-1 text-sm text-text-muted">Live overview across every server you monitor.</p>
      </div>

      <StatTiles />

      <div className="grid grid-cols-1 gap-6 xl:grid-cols-2">
        <ServerStatusGrid />
        <RaidAlertFeed />
      </div>
    </div>
  );
}
