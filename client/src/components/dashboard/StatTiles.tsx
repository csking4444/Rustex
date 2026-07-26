import type { LucideIcon } from "lucide-react";
import { Activity, Server, Siren, Users } from "lucide-react";
import { useServers } from "@/hooks/useServers";
import { useRecentRaidEvents } from "@/hooks/useRaidEvents";

interface TileProps {
  icon: LucideIcon;
  label: string;
  value: string | number;
  accent?: "default" | "critical";
}

function Tile({ icon: Icon, label, value, accent = "default" }: TileProps) {
  return (
    <div className="glass-panel flex items-center gap-4 p-5">
      <div
        className={`flex h-11 w-11 shrink-0 items-center justify-center rounded-xl border ${
          accent === "critical" ? "bg-critical/15 border-critical/30 text-critical" : "bg-blood/15 border-blood/30 text-blood-light"
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

export function StatTiles() {
  const { data: servers } = useServers();
  const { data: raidEvents } = useRecentRaidEvents(50);

  const onlineCount = servers?.filter((s) => s.status === "Online").length ?? 0;
  const activeRaids = raidEvents?.filter((e) => e.status === "Active").length ?? 0;

  return (
    <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
      <Tile icon={Server} label="Servers Online" value={servers ? `${onlineCount}/${servers.length}` : "—"} />
      <Tile icon={Users} label="Teams" value={"—"} />
      <Tile icon={Siren} label="Active Raids" value={activeRaids} accent={activeRaids > 0 ? "critical" : "default"} />
      <Tile icon={Activity} label="Events (24h)" value={raidEvents?.length ?? 0} />
    </div>
  );
}
