import { useState, type FormEvent } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { motion } from "framer-motion";
import { Plus, Server as ServerIcon, Star, Trash2, Wifi, WifiOff } from "lucide-react";
import { apiClient } from "@/lib/apiClient";
import { useServers } from "@/hooks/useServers";
import { Card, CardHeader } from "@/components/ui/Card";
import { SkeletonList } from "@/components/ui/Skeleton";
import type { RustServerSummary } from "@/types";

export default function ServersPage() {
  const { data: servers, isLoading } = useServers();
  const [showForm, setShowForm] = useState(false);

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-semibold text-text-primary">Servers</h1>
          <p className="mt-1 text-sm text-text-muted">Manage the Rust servers you monitor.</p>
        </div>
        <button className="btn-primary" onClick={() => setShowForm((v) => !v)}>
          <Plus className="h-4 w-4" />
          Add Server
        </button>
      </div>

      {showForm && <AddServerForm onDone={() => setShowForm(false)} />}

      <Card>
        <CardHeader title="All Servers" subtitle={servers ? `${servers.length} total` : undefined} />

        {isLoading && <SkeletonList rows={4} />}

        {!isLoading && servers?.length === 0 && (
          <div className="flex flex-col items-center justify-center gap-2 py-12 text-text-muted">
            <ServerIcon className="h-8 w-8" />
            <p className="text-sm">No servers yet. Add your first one above.</p>
          </div>
        )}

        {!isLoading && servers && servers.length > 0 && (
          <ul className="flex flex-col gap-2">
            {servers.map((server) => (
              <ServerRow key={server.id} server={server} />
            ))}
          </ul>
        )}
      </Card>
    </div>
  );
}

function AddServerForm({ onDone }: { onDone: () => void }) {
  const queryClient = useQueryClient();
  const [name, setName] = useState("");
  const [ipAddress, setIpAddress] = useState("");
  const [gamePort, setGamePort] = useState("28015");

  const createServer = useMutation({
    mutationFn: async () =>
      apiClient.post("/servers", {
        name,
        ipAddress,
        gamePort: Number(gamePort),
      }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["servers"] });
      onDone();
    },
  });

  function handleSubmit(e: FormEvent) {
    e.preventDefault();
    createServer.mutate();
  }

  return (
    <motion.form
      initial={{ opacity: 0, height: 0 }}
      animate={{ opacity: 1, height: "auto" }}
      exit={{ opacity: 0, height: 0 }}
      onSubmit={handleSubmit}
      className="glass-panel grid grid-cols-1 gap-4 p-5 sm:grid-cols-4"
    >
      <input
        required
        placeholder="Server name"
        value={name}
        onChange={(e) => setName(e.target.value)}
        className="rounded-xl border border-white/10 bg-base-800/60 px-3 py-2 text-sm text-text-primary placeholder:text-text-muted focus:outline-none focus:ring-2 focus:ring-blood-light/60"
      />
      <input
        required
        placeholder="IP address"
        value={ipAddress}
        onChange={(e) => setIpAddress(e.target.value)}
        className="rounded-xl border border-white/10 bg-base-800/60 px-3 py-2 text-sm text-text-primary placeholder:text-text-muted focus:outline-none focus:ring-2 focus:ring-blood-light/60"
      />
      <input
        required
        type="number"
        placeholder="Game port"
        value={gamePort}
        onChange={(e) => setGamePort(e.target.value)}
        className="rounded-xl border border-white/10 bg-base-800/60 px-3 py-2 text-sm text-text-primary placeholder:text-text-muted focus:outline-none focus:ring-2 focus:ring-blood-light/60"
      />
      <button type="submit" disabled={createServer.isPending} className="btn-primary">
        {createServer.isPending ? "Adding..." : "Add"}
      </button>
    </motion.form>
  );
}

function ServerRow({ server }: { server: RustServerSummary }) {
  const queryClient = useQueryClient();

  const deleteServer = useMutation({
    mutationFn: async () => apiClient.delete(`/servers/${server.id}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["servers"] }),
  });

  return (
    <li className="flex items-center justify-between rounded-xl border border-white/5 bg-base-800/40 px-4 py-3">
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
            {server.status === "Online" && server.playerCount !== null
              ? ` · ${server.playerCount}/${server.maxPlayers ?? "?"} players · ${server.pingMs}ms`
              : ""}
          </p>
        </div>
      </div>
      <button
        onClick={() => deleteServer.mutate()}
        className="text-text-muted transition-colors hover:text-critical"
        aria-label="Remove server"
      >
        <Trash2 className="h-4 w-4" />
      </button>
    </li>
  );
}
