import { useQuery } from "@tanstack/react-query";
import { apiClient } from "@/lib/apiClient";
import type { RaidEventSummary } from "@/types";

export function useRecentRaidEvents(limit = 20) {
  return useQuery({
    queryKey: ["raid-events", "recent", limit],
    queryFn: async () =>
      (await apiClient.get<RaidEventSummary[]>("/raid-events/recent", { params: { limit } })).data,
    refetchInterval: 30_000,
  });
}
