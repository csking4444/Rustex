import { Skull, Users } from "lucide-react";
import { Card, CardHeader } from "@/components/ui/Card";
import { SkeletonList } from "@/components/ui/Skeleton";
import { useRustPlusTeamState } from "@/hooks/useRustPlusTeam";

export function TeamTrackingTab({ serverId }: { serverId: string }) {
  const { data: members, isLoading } = useRustPlusTeamState(serverId);

  return (
    <Card>
      <CardHeader
        title="Team Tracker"
        subtitle={members ? `${members.filter((m) => m.isOnline).length}/${members.length} online` : undefined}
      />

      {isLoading && <SkeletonList rows={4} />}

      {!isLoading && (!members || members.length === 0) && (
        <div className="flex flex-col items-center justify-center gap-2 py-12 text-text-muted">
          <Users className="h-8 w-8" />
          <p className="text-sm">No team data yet — it appears once Rust+ reports your team roster.</p>
        </div>
      )}

      {!isLoading && members && members.length > 0 && (
        <ul className="flex flex-col gap-2">
          {members.map((m) => (
            <li
              key={m.steamId}
              className="flex items-center justify-between rounded-xl border border-white/5 bg-base-800/40 px-4 py-3"
            >
              <div className="flex items-center gap-3">
                <span className={m.isOnline ? "badge-success" : "badge"}>{m.isOnline ? "Online" : "Offline"}</span>
                <div>
                  <p className="flex items-center gap-1.5 text-sm font-medium text-text-primary">
                    {m.name}
                    {!m.isAlive && <Skull className="h-3.5 w-3.5 text-critical" />}
                  </p>
                  <p className="text-xs text-text-muted">
                    {m.lastGrid ?? "Unknown grid"} · last seen {new Date(m.lastSeenAt).toLocaleTimeString()}
                  </p>
                </div>
              </div>
            </li>
          ))}
        </ul>
      )}
    </Card>
  );
}
