import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "@/lib/apiClient";
import type { RaidAlarmSettings } from "@/types";

export function useRaidAlarmSettings(serverId: string | null) {
  return useQuery({
    queryKey: ["raid-alarm-settings", serverId],
    queryFn: async () => (await apiClient.get<RaidAlarmSettings>(`/servers/${serverId}/raid-alarm-settings`)).data,
    enabled: serverId !== null,
  });
}

export function useUpdateRaidAlarmSettings(serverId: string | null) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (settings: Omit<RaidAlarmSettings, "serverId" | "updatedAt">) =>
      (await apiClient.put<RaidAlarmSettings>(`/servers/${serverId}/raid-alarm-settings`, settings)).data,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["raid-alarm-settings", serverId] });
    },
  });
}
