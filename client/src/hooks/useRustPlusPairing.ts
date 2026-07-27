import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "@/lib/apiClient";
import type { RustPlusPairing } from "@/types";

export function useRustPlusPairing(serverId: string | null) {
  return useQuery({
    queryKey: ["rustplus", "pairing", serverId],
    queryFn: async () => {
      try {
        return (await apiClient.get<RustPlusPairing>(`/servers/${serverId}/rustplus/pairing`)).data;
      } catch (err: unknown) {
        if ((err as { response?: { status?: number } })?.response?.status === 404) return null;
        throw err;
      }
    },
    enabled: serverId !== null,
  });
}

export function useSaveRustPlusPairing(serverId: string | null) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (request: { playerId: string; playerToken: number; serverIp: string; serverPort: number }) =>
      (await apiClient.post<RustPlusPairing>(`/servers/${serverId}/rustplus/pairing`, request)).data,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["rustplus", "pairing", serverId] });
    },
  });
}

export function useDeleteRustPlusPairing(serverId: string | null) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async () => apiClient.delete(`/servers/${serverId}/rustplus/pairing`),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["rustplus", "pairing", serverId] });
    },
  });
}
