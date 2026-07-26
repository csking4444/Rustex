import { useEffect, useState } from "react";
import { Crosshair, MapPin, Trash2, Users, Building2, Skull } from "lucide-react";
import { Card, CardHeader } from "@/components/ui/Card";
import { useServers } from "@/hooks/useServers";
import { useCreateMarker, useDeleteMarker, useMarkers } from "@/hooks/useMap";
import { InteractiveMap, MARKER_COLORS } from "@/components/map/InteractiveMap";
import type { MapMarker, MarkerType } from "@/types";

const MARKER_TYPES: { type: MarkerType; label: string; icon: typeof MapPin }[] = [
  { type: "Raid", label: "Raid", icon: Skull },
  { type: "Team", label: "Team", icon: Users },
  { type: "Player", label: "Player", icon: Crosshair },
  { type: "Monument", label: "Monument", icon: Building2 },
  { type: "Custom", label: "Custom", icon: MapPin },
];

export default function MapsPage() {
  const { data: servers, isLoading: serversLoading } = useServers();
  const [selectedServerId, setSelectedServerId] = useState<string | null>(null);
  const [placingType, setPlacingType] = useState<MarkerType | null>(null);
  const [selectedMarker, setSelectedMarker] = useState<MapMarker | null>(null);

  useEffect(() => {
    if (!selectedServerId && servers && servers.length > 0) setSelectedServerId(servers[0].id);
  }, [servers, selectedServerId]);

  const server = servers?.find((s) => s.id === selectedServerId);
  const worldSize = server?.worldSize ?? 4000;

  const { data: markers } = useMarkers(selectedServerId);
  const createMarker = useCreateMarker(selectedServerId);
  const deleteMarker = useDeleteMarker(selectedServerId);

  function handlePlaceMarker(x: number, y: number) {
    if (!placingType) return;
    createMarker.mutate({ type: placingType, x, y, label: null, color: null, isShared: true });
    setPlacingType(null);
  }

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-semibold text-text-primary">Interactive Map</h1>
          <p className="mt-1 text-sm text-text-muted">
            Custom coordinate map — pan by dragging, zoom with the scroll wheel. No real Rust map imagery is
            available (each seed generates its own terrain and there's no public tile source), so this is a grid
            with shared markers rather than a satellite view.
          </p>
        </div>

        {servers && servers.length > 0 && (
          <select
            value={selectedServerId ?? ""}
            onChange={(e) => {
              setSelectedServerId(e.target.value);
              setSelectedMarker(null);
              setPlacingType(null);
            }}
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
            <MapPin className="h-8 w-8" />
            <p className="text-sm">Add a server first to place markers for it.</p>
          </div>
        </Card>
      )}

      {selectedServerId && (
        <div className="grid grid-cols-1 gap-6 xl:grid-cols-[1fr_280px]">
          <div className="flex flex-col gap-3">
            <div className="flex flex-wrap gap-2">
              {MARKER_TYPES.map(({ type, label, icon: Icon }) => (
                <button
                  key={type}
                  onClick={() => setPlacingType(placingType === type ? null : type)}
                  className={`flex items-center gap-1.5 rounded-xl border px-3 py-1.5 text-xs font-medium transition-colors ${
                    placingType === type
                      ? "border-blood/50 bg-blood/15 text-white"
                      : "border-white/10 bg-base-800/60 text-text-secondary hover:border-white/20"
                  }`}
                >
                  <Icon className="h-3.5 w-3.5" style={{ color: MARKER_COLORS[type] }} />
                  {placingType === type ? `Click map to place ${label}` : label}
                </button>
              ))}
            </div>

            <InteractiveMap
              worldSize={worldSize}
              markers={markers ?? []}
              placingType={placingType}
              onPlaceMarker={handlePlaceMarker}
              onSelectMarker={setSelectedMarker}
            />
          </div>

          <div className="flex flex-col gap-4">
            {selectedMarker && (
              <Card>
                <CardHeader title={selectedMarker.type} subtitle={`(${Math.round(selectedMarker.x)}, ${Math.round(selectedMarker.y)})`} />
                <button
                  onClick={() => {
                    deleteMarker.mutate(selectedMarker.id);
                    setSelectedMarker(null);
                  }}
                  className="btn-ghost w-full gap-2 text-critical hover:border-critical/40"
                >
                  <Trash2 className="h-4 w-4" />
                  Remove marker
                </button>
              </Card>
            )}

            <Card>
              <CardHeader title="Markers" subtitle={markers ? `${markers.length} placed` : undefined} />
              {markers && markers.length === 0 && <p className="text-xs text-text-muted">None yet — pick a type above and click the map.</p>}
              {markers && markers.length > 0 && (
                <ul className="flex flex-col gap-2">
                  {markers.map((marker) => (
                    <li key={marker.id}>
                      <button
                        onClick={() => setSelectedMarker(marker)}
                        className="flex w-full items-center gap-2 rounded-lg border border-white/5 bg-base-800/40 px-3 py-2 text-left text-xs text-text-secondary hover:border-white/10"
                      >
                        <span
                          className="h-2.5 w-2.5 shrink-0 rounded-full"
                          style={{ backgroundColor: marker.color ?? MARKER_COLORS[marker.type] }}
                        />
                        {marker.label ?? marker.type}
                      </button>
                    </li>
                  ))}
                </ul>
              )}
            </Card>
          </div>
        </div>
      )}
    </div>
  );
}
