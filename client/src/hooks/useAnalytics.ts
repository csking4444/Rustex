import { useQuery } from "@tanstack/react-query";
import { apiClient } from "@/lib/apiClient";
import type { AnalyticsSummary } from "@/types";

export function useAnalyticsSummary(serverId: string | null, days = 7) {
  return useQuery({
    queryKey: ["analytics-summary", serverId, days],
    queryFn: async () =>
      (await apiClient.get<AnalyticsSummary>(`/servers/${serverId}/analytics/summary`, { params: { days } })).data,
    enabled: serverId !== null,
  });
}
