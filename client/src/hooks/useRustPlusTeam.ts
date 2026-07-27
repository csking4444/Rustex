import { useQuery } from "@tanstack/react-query";
import { apiClient } from "@/lib/apiClient";
import type { RustPlusTeamMemberState } from "@/types";

export function useRustPlusTeamState(serverId: string | null) {
  return useQuery({
    queryKey: ["rustplus", "team-state", serverId],
    queryFn: async () => (await apiClient.get<RustPlusTeamMemberState[]>(`/servers/${serverId}/rustplus/team-state`)).data,
    enabled: serverId !== null,
    refetchInterval: 15_000,
  });
}
