import { useState } from "react";
import { Search, Store } from "lucide-react";
import { Card, CardHeader } from "@/components/ui/Card";
import { SkeletonList } from "@/components/ui/Skeleton";
import { useRustPlusVendingSearch } from "@/hooks/useRustPlusVending";

export function VendingSearchTab({ serverId }: { serverId: string }) {
  const [q, setQ] = useState("");
  const [inStockOnly, setInStockOnly] = useState(true);
  const { data: results, isLoading } = useRustPlusVendingSearch(serverId, { q: q || undefined, inStockOnly });

  return (
    <Card>
      <CardHeader title="Vending Search" subtitle="Reads the last poll — never round-trips to the game server per keystroke." />

      <div className="mb-4 flex flex-wrap items-center gap-3">
        <div className="relative flex-1 min-w-[200px]">
          <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-text-muted" />
          <input
            value={q}
            onChange={(e) => setQ(e.target.value)}
            placeholder="Search item name..."
            className="w-full rounded-xl border border-white/10 bg-base-800/60 py-2 pl-9 pr-3 text-sm text-text-primary focus:outline-none focus:ring-2 focus:ring-blood-light/60"
          />
        </div>
        <label className="flex items-center gap-2 text-xs text-text-muted">
          <input
            type="checkbox"
            checked={inStockOnly}
            onChange={(e) => setInStockOnly(e.target.checked)}
            className="h-4 w-4 rounded border-white/20 bg-base-800 accent-blood"
          />
          In stock only
        </label>
      </div>

      {isLoading && <SkeletonList rows={4} />}

      {!isLoading && (!results || results.length === 0) && (
        <div className="flex flex-col items-center justify-center gap-2 py-12 text-text-muted">
          <Store className="h-8 w-8" />
          <p className="text-sm">No listings match yet — the vending poll runs every 60s.</p>
        </div>
      )}

      {!isLoading && results && results.length > 0 && (
        <ul className="flex flex-col gap-2">
          {results.map((r, i) => (
            <li
              key={`${r.markerId}-${r.itemId}-${i}`}
              className="flex items-center justify-between rounded-xl border border-white/5 bg-base-800/40 px-4 py-3"
            >
              <div>
                <p className="text-sm font-medium text-text-primary">{r.itemName}</p>
                <p className="text-xs text-text-muted">
                  {r.machineName ?? "Vending Machine"} · {r.grid ?? "Unknown grid"} · {r.amountInStock} in stock
                </p>
              </div>
              <span className="font-mono text-sm font-semibold text-text-primary">
                {r.costPerItem} {r.currencyName}
              </span>
            </li>
          ))}
        </ul>
      )}
    </Card>
  );
}
