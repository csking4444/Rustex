import { useQuery } from "@tanstack/react-query";
import { apiClient } from "@/lib/apiClient";
import type { TeamSummary } from "@/types";

export function useTeams() {
  return useQuery({
    queryKey: ["teams"],
    queryFn: async () => (await apiClient.get<TeamSummary[]>("/teams")).data,
  });
}
