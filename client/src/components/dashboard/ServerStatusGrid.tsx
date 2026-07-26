import { Server as ServerIcon, Star, Wifi, WifiOff } from "lucide-react";
import { Card, CardHeader } from "@/components/ui/Card";
import { SkeletonList } from "@/components/ui/Skeleton";
import { useServers } from "@/hooks/useServers";

export function ServerStatusGrid() {
  const { data: servers, isLoading, isError } = useServers();

  return (
    <Card>
      <CardHeader title="Server Status" subtitle="Your monitored Rust servers" />

      {isLoading && <SkeletonList rows={3} />}

      {isError && <p className="text-sm text-critical">Couldn't load servers. Retrying automatically.</p>}

      {!isLoading && !isError && servers?.length === 0 && (
        <div className="flex flex-col items-center justify-center gap-2 py-8 text-text-muted">
          <ServerIcon className="h-8 w-8" />
          <p className="text-sm">No servers yet — add your first Rust server to start monitoring.</p>
        </div>
      )}

      {!isLoading && servers && servers.length > 0 && (
        <ul className="flex flex-col gap-2">
          {servers.map((server) => (
            <li
              key={server.id}
              className="flex items-center justify-between rounded-xl border border-white/5 bg-base-800/40 px-4 py-3"
            >
              <div className="flex items-center gap-3">
                {server.status === "Online" ? (
                  <Wifi className="h-4 w-4 text-success" />
                ) : (
                  <WifiOff className="h-4 w-4 text-text-muted" />
                )}
                <div>
                  <p className="flex items-center gap-1.5 text-sm font-medium text-text-primary">
                    {server.name}
                    {server.isFavorite && <Star className="h-3.5 w-3.5 fill-warning text-warning" />}
                  </p>
                  <p className="text-xs text-text-muted">
                    {server.ipAddress}:{server.gamePort}
                    {server.mapName ? ` · ${server.mapName}` : ""}
                    {server.status === "Online" && server.playerCount !== null
                      ? ` · ${server.playerCount}/${server.maxPlayers ?? "?"} players`
                      : ""}
                    {server.status === "Online" && server.pingMs !== null ? ` · ${server.pingMs}ms` : ""}
                  </p>
                </div>
              </div>
              <span
                className={
                  server.status === "Online" ? "badge-success" : server.status === "Offline" ? "badge-critical" : "badge-info"
                }
              >
                {server.status}
              </span>
            </li>
          ))}
        </ul>
      )}
    </Card>
  );
}
