import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "@/lib/apiClient";
import type { RustPlusSmartDevice, SmartDeviceKind } from "@/types";

export function useRustPlusDevices(serverId: string | null) {
  return useQuery({
    queryKey: ["rustplus", "devices", serverId],
    queryFn: async () => (await apiClient.get<RustPlusSmartDevice[]>(`/servers/${serverId}/rustplus/devices`)).data,
    enabled: serverId !== null,
    refetchInterval: 15_000,
  });
}

export function useCreateRustPlusDevice(serverId: string | null) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (input: { entityId: number; type: SmartDeviceKind; name: string }) =>
      (await apiClient.post<RustPlusSmartDevice>(`/servers/${serverId}/rustplus/devices`, input)).data,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["rustplus", "devices", serverId] });
    },
  });
}

export function useUpdateRustPlusDevice(serverId: string | null) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, ...input }: { id: string; name: string; alarmRaisesRaidEvent: boolean }) =>
      (await apiClient.put<RustPlusSmartDevice>(`/servers/${serverId}/rustplus/devices/${id}`, input)).data,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["rustplus", "devices", serverId] });
    },
  });
}

export function useDeleteRustPlusDevice(serverId: string | null) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => apiClient.delete(`/servers/${serverId}/rustplus/devices/${id}`),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["rustplus", "devices", serverId] });
    },
  });
}

export function useSetRustPlusDeviceValue(serverId: string | null) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, value }: { id: string; value: boolean }) =>
      apiClient.post(`/servers/${serverId}/rustplus/devices/${id}/value`, { value }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["rustplus", "devices", serverId] });
    },
  });
}
