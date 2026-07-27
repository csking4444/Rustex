import { useEffect, useState } from "react";
import { Cpu, MessageSquare, Radio, Store, Users } from "lucide-react";
import { Card } from "@/components/ui/Card";
import { useServers } from "@/hooks/useServers";
import { useRustPlusPairing } from "@/hooks/useRustPlusPairing";
import { useRustPlusRealtime } from "@/hooks/useRustPlusRealtime";
import { RustPlusAccountSetup } from "@/components/rustplus/RustPlusAccountSetup";
import { ManualPairingForm } from "@/components/rustplus/ManualPairingForm";
import { TeamTrackingTab } from "@/components/rustplus/TeamTrackingTab";
import { VendingSearchTab } from "@/components/rustplus/VendingSearchTab";
import { ShopAlertsTab } from "@/components/rustplus/ShopAlertsTab";
import { SmartDevicesTab } from "@/components/rustplus/SmartDevicesTab";
import { ChatAssistantTab } from "@/components/rustplus/ChatAssistantTab";

const TABS = [
  { key: "team", label: "Team", icon: Users },
  { key: "vending", label: "Vending", icon: Store },
  { key: "alerts", label: "Shop Alerts", icon: Radio },
  { key: "devices", label: "Devices", icon: Cpu },
  { key: "chat", label: "Chat", icon: MessageSquare },
] as const;

type TabKey = (typeof TABS)[number]["key"];

export default function RustPlusPage() {
  const { data: servers, isLoading: serversLoading } = useServers();
  const [selectedServerId, setSelectedServerId] = useState<string | null>(null);
  const [activeTab, setActiveTab] = useState<TabKey>("team");

  useEffect(() => {
    if (!selectedServerId && servers && servers.length > 0) {
      setSelectedServerId(servers[0].id);
    }
  }, [servers, selectedServerId]);

  const { data: pairing, isLoading: pairingLoading } = useRustPlusPairing(selectedServerId);
  useRustPlusRealtime(selectedServerId);

  const selectedServer = servers?.find((s) => s.id === selectedServerId);

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-semibold text-text-primary">Rust+</h1>
          <p className="mt-1 text-sm text-text-muted">Team tracking, vending search, shop alerts, smart devices, and chat — live from the game.</p>
        </div>

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
      </div>

      {!serversLoading && (!servers || servers.length === 0) && (
        <Card>
          <div className="flex flex-col items-center justify-center gap-2 py-12 text-text-muted">
            <Radio className="h-8 w-8" />
            <p className="text-sm">Add a server first to use Rust+ features.</p>
          </div>
        </Card>
      )}

      <RustPlusAccountSetup />

      {selectedServerId && selectedServer && !pairingLoading && !pairing && (
        <ManualPairingForm serverId={selectedServerId} serverIp={selectedServer.ipAddress} serverPort={selectedServer.gamePort} />
      )}

      {selectedServerId && pairing && (
        <div className="flex flex-col gap-4">
          <div className="flex flex-wrap gap-2">
            {TABS.map(({ key, label, icon: Icon }) => (
              <button
                key={key}
                type="button"
                onClick={() => setActiveTab(key)}
                className={[
                  "flex items-center gap-2 rounded-xl px-4 py-2 text-sm font-medium transition-colors",
                  activeTab === key
                    ? "bg-blood/15 text-white border border-blood/30"
                    : "text-text-secondary hover:bg-white/5 hover:text-white border border-transparent",
                ].join(" ")}
              >
                <Icon className="h-4 w-4" />
                {label}
              </button>
            ))}
          </div>

          {activeTab === "team" && <TeamTrackingTab serverId={selectedServerId} />}
          {activeTab === "vending" && <VendingSearchTab serverId={selectedServerId} />}
          {activeTab === "alerts" && <ShopAlertsTab serverId={selectedServerId} />}
          {activeTab === "devices" && <SmartDevicesTab serverId={selectedServerId} />}
          {activeTab === "chat" && <ChatAssistantTab serverId={selectedServerId} />}
        </div>
      )}
    </div>
  );
}
