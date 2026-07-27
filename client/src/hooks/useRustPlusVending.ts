import { useQuery } from "@tanstack/react-query";
import { apiClient } from "@/lib/apiClient";
import type { RustPlusVendingSearchResult } from "@/types";

export function useRustPlusVendingSearch(
  serverId: string | null,
  params: { q?: string; maxCost?: number; inStockOnly?: boolean },
) {
  return useQuery({
    queryKey: ["rustplus", "vending", "search", serverId, params],
    queryFn: async () =>
      (
        await apiClient.get<RustPlusVendingSearchResult[]>(`/servers/${serverId}/rustplus/vending/search`, {
          params,
        })
      ).data,
    enabled: serverId !== null,
  });
}
