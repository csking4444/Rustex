interface BarChartProps {
  data: { label: string; value: number }[];
  color?: string;
  height?: number;
}

export function BarChart({ data, color = "#7A0000", height = 180 }: BarChartProps) {
  const max = Math.max(1, ...data.map((d) => d.value));

  return (
    <div className="flex items-end gap-1" style={{ height }}>
      {data.map((d, i) => (
        <div key={i} className="flex h-full flex-1 flex-col items-center justify-end gap-1.5">
          <span className="text-[10px] text-text-muted">{d.value > 0 ? d.value : ""}</span>
          <div className="flex w-full flex-1 items-end">
            <div
              className="w-full rounded-t-sm transition-all duration-300"
              style={{
                height: `${Math.max((d.value / max) * 100, d.value > 0 ? 3 : 0)}%`,
                backgroundColor: color,
              }}
              title={`${d.label}: ${d.value}`}
            />
          </div>
          <span className="whitespace-nowrap text-[10px] text-text-muted">{d.label}</span>
        </div>
      ))}
    </div>
  );
}
