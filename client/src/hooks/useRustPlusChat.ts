import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "@/lib/apiClient";
import type { RustPlusChatMessage } from "@/types";

export function useRustPlusChat(serverId: string | null) {
  return useQuery({
    queryKey: ["rustplus", "chat", serverId],
    queryFn: async () => (await apiClient.get<RustPlusChatMessage[]>(`/servers/${serverId}/rustplus/chat`)).data,
    enabled: serverId !== null,
    refetchInterval: 8_000,
  });
}

export function useSendRustPlusChat(serverId: string | null) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (message: string) => apiClient.post(`/servers/${serverId}/rustplus/chat`, { message }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["rustplus", "chat", serverId] });
    },
  });
}
