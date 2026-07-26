import { useRef, useState, type MouseEvent, type WheelEvent } from "react";
import type { MapMarker, MarkerType } from "@/types";

interface ViewBox {
  x: number;
  y: number;
  size: number;
}

export const MARKER_COLORS: Record<MarkerType, string> = {
  Raid: "#C1121F",
  Team: "#4A7A96",
  Player: "#1F8A3B",
  Custom: "#D89A2B",
  Monument: "#8A8A8A",
};

interface InteractiveMapProps {
  worldSize: number;
  markers: MapMarker[];
  placingType: MarkerType | null;
  onPlaceMarker: (x: number, y: number) => void;
  onSelectMarker: (marker: MapMarker) => void;
}

/**
 * A custom pan/zoom coordinate-plane map, not a real Rust satellite-style map. There's no
 * public tile source for Rust maps (each server's terrain is procedurally generated from its
 * seed, and Facepunch doesn't expose map imagery via any API) — MapData.imageUrl exists for a
 * server-supplied image (e.g. from RustMaps.com) to be layered in later, but rendering markers
 * against an honest grid beats pretending a real map tile layer works today.
 */
export function InteractiveMap({ worldSize, markers, placingType, onPlaceMarker, onSelectMarker }: InteractiveMapProps) {
  const svgRef = useRef<SVGSVGElement>(null);
  const [viewBox, setViewBox] = useState<ViewBox>({ x: 0, y: 0, size: worldSize });
  const [isDragging, setIsDragging] = useState(false);
  const dragState = useRef<{ startClientX: number; startClientY: number; startViewBox: ViewBox } | null>(null);

  function toSvgPoint(clientX: number, clientY: number): { x: number; y: number } {
    const svg = svgRef.current;
    if (!svg) return { x: 0, y: 0 };
    const point = svg.createSVGPoint();
    point.x = clientX;
    point.y = clientY;
    const ctm = svg.getScreenCTM();
    if (!ctm) return { x: 0, y: 0 };
    const transformed = point.matrixTransform(ctm.inverse());
    return { x: transformed.x, y: transformed.y };
  }

  function handleBackgroundMouseDown(e: MouseEvent<SVGSVGElement>) {
    dragState.current = { startClientX: e.clientX, startClientY: e.clientY, startViewBox: viewBox };
    setIsDragging(false);
  }

  function handleBackgroundMouseMove(e: MouseEvent<SVGSVGElement>) {
    if (!dragState.current || !svgRef.current) return;

    const dxClient = e.clientX - dragState.current.startClientX;
    const dyClient = e.clientY - dragState.current.startClientY;
    if (Math.abs(dxClient) > 3 || Math.abs(dyClient) > 3) setIsDragging(true);

    const rect = svgRef.current.getBoundingClientRect();
    const scaleX = dragState.current.startViewBox.size / rect.width;
    const scaleY = dragState.current.startViewBox.size / rect.height;

    setViewBox({
      x: dragState.current.startViewBox.x - dxClient * scaleX,
      y: dragState.current.startViewBox.y - dyClient * scaleY,
      size: dragState.current.startViewBox.size,
    });
  }

  function handleBackgroundMouseUp(e: MouseEvent<SVGSVGElement>) {
    const wasDragging = isDragging;
    dragState.current = null;
    setIsDragging(false);

    if (!wasDragging && placingType) {
      const point = toSvgPoint(e.clientX, e.clientY);
      onPlaceMarker(Math.round(point.x), Math.round(point.y));
    }
  }

  function handleWheel(e: WheelEvent<SVGSVGElement>) {
    e.preventDefault();
    const point = toSvgPoint(e.clientX, e.clientY);
    const factor = e.deltaY > 0 ? 1.15 : 1 / 1.15;
    const newSize = Math.min(worldSize * 2, Math.max(worldSize / 20, viewBox.size * factor));

    setViewBox({
      x: point.x - (point.x - viewBox.x) * (newSize / viewBox.size),
      y: point.y - (point.y - viewBox.y) * (newSize / viewBox.size),
      size: newSize,
    });
  }

  const gridStep = worldSize / 20;
  const markerRadius = viewBox.size / 80;

  return (
    <div className="relative overflow-hidden rounded-xl border border-white/10 bg-base-950">
      <svg
        ref={svgRef}
        viewBox={`${viewBox.x} ${viewBox.y} ${viewBox.size} ${viewBox.size}`}
        className={`h-[560px] w-full select-none ${
          placingType ? "cursor-crosshair" : isDragging ? "cursor-grabbing" : "cursor-grab"
        }`}
        onMouseDown={handleBackgroundMouseDown}
        onMouseMove={handleBackgroundMouseMove}
        onMouseUp={handleBackgroundMouseUp}
        onMouseLeave={() => {
          dragState.current = null;
          setIsDragging(false);
        }}
        onWheel={handleWheel}
      >
        <defs>
          <pattern id="rustex-map-grid" width={gridStep} height={gridStep} patternUnits="userSpaceOnUse">
            <path d={`M ${gridStep} 0 L 0 0 0 ${gridStep}`} fill="none" stroke="#242424" strokeWidth={worldSize / 2000} />
          </pattern>
        </defs>

        <rect x={0} y={0} width={worldSize} height={worldSize} fill="#141414" />
        <rect x={0} y={0} width={worldSize} height={worldSize} fill="url(#rustex-map-grid)" />
        <rect x={0} y={0} width={worldSize} height={worldSize} fill="none" stroke="#7A0000" strokeWidth={worldSize / 500} />

        {markers.map((marker) => (
          <g
            key={marker.id}
            onMouseDown={(e) => e.stopPropagation()}
            onClick={(e) => {
              e.stopPropagation();
              onSelectMarker(marker);
            }}
            className="cursor-pointer"
          >
            <circle
              cx={marker.x}
              cy={marker.y}
              r={markerRadius}
              fill={marker.color ?? MARKER_COLORS[marker.type]}
              stroke="#0B0B0B"
              strokeWidth={viewBox.size / 800}
            />
            {marker.label && (
              <text
                x={marker.x}
                y={marker.y - markerRadius * 1.8}
                fontSize={viewBox.size / 60}
                fill="#FFFFFF"
                textAnchor="middle"
              >
                {marker.label}
              </text>
            )}
          </g>
        ))}
      </svg>

      <button
        onClick={() => setViewBox({ x: 0, y: 0, size: worldSize })}
        className="absolute bottom-3 right-3 rounded-lg border border-white/10 bg-base-900/80 px-3 py-1.5 text-xs text-text-secondary backdrop-blur transition-colors hover:text-white"
      >
        Reset view
      </button>
    </div>
  );
}
