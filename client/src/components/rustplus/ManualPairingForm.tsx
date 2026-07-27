import { useState, type FormEvent } from "react";
import { Wifi } from "lucide-react";
import { Card, CardHeader } from "@/components/ui/Card";
import { useSaveRustPlusPairing } from "@/hooks/useRustPlusPairing";

/** Shown for a server with no saved pairing yet. playerId/playerToken come from any community
 * Rust+ pairing tool (or the account-level auto-pair flow, once that registers this server). */
export function ManualPairingForm({ serverId, serverIp, serverPort }: { serverId: string; serverIp: string; serverPort: number }) {
  const savePairing = useSaveRustPlusPairing(serverId);
  const [playerId, setPlayerId] = useState("");
  const [playerToken, setPlayerToken] = useState("");
  const [ip, setIp] = useState(serverIp);
  const [port, setPort] = useState(serverPort);

  function handleSubmit(e: FormEvent) {
    e.preventDefault();
    const token = Number(playerToken);
    if (!playerId.trim() || !Number.isFinite(token)) return;
    savePairing.mutate({ playerId: playerId.trim(), playerToken: token, serverIp: ip, serverPort: port });
  }

  return (
    <Card>
      <CardHeader
        title="Pair this server with Rust+"
        subtitle="Paste the playerId/playerToken from a Rust+ pairing tool, or set up auto-pairing above."
      />

      <form onSubmit={handleSubmit} className="flex flex-col gap-3">
        <div className="grid grid-cols-2 gap-3">
          <label className="flex flex-col gap-1">
            <span className="text-xs text-text-muted">Player ID (Steam64)</span>
            <input
              value={playerId}
              onChange={(e) => setPlayerId(e.target.value)}
              placeholder="76561198000000000"
              className="rounded-xl border border-white/10 bg-base-800/60 px-3 py-2 text-sm text-text-primary focus:outline-none focus:ring-2 focus:ring-blood-light/60"
            />
          </label>
          <label className="flex flex-col gap-1">
            <span className="text-xs text-text-muted">Player Token</span>
            <input
              value={playerToken}
              onChange={(e) => setPlayerToken(e.target.value)}
              placeholder="-123456789"
              className="rounded-xl border border-white/10 bg-base-800/60 px-3 py-2 text-sm text-text-primary focus:outline-none focus:ring-2 focus:ring-blood-light/60"
            />
          </label>
        </div>

        <div className="grid grid-cols-2 gap-3">
          <label className="flex flex-col gap-1">
            <span className="text-xs text-text-muted">Rust+ server IP</span>
            <input
              value={ip}
              onChange={(e) => setIp(e.target.value)}
              className="rounded-xl border border-white/10 bg-base-800/60 px-3 py-2 text-sm text-text-primary focus:outline-none focus:ring-2 focus:ring-blood-light/60"
            />
          </label>
          <label className="flex flex-col gap-1">
            <span className="text-xs text-text-muted">Rust+ companion port</span>
            <input
              type="number"
              value={port}
              onChange={(e) => setPort(Number(e.target.value))}
              className="rounded-xl border border-white/10 bg-base-800/60 px-3 py-2 text-sm text-text-primary focus:outline-none focus:ring-2 focus:ring-blood-light/60"
            />
          </label>
        </div>

        <button type="submit" disabled={savePairing.isPending} className="btn-primary self-start">
          <Wifi className="h-4 w-4" />
          {savePairing.isPending ? "Pairing..." : "Pair Server"}
        </button>

        {savePairing.isError && <p className="text-xs text-critical">Couldn't save this pairing — double-check the token.</p>}
      </form>
    </Card>
  );
}
