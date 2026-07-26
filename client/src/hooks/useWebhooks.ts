import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "@/lib/apiClient";
import type { Webhook } from "@/types";

export function useWebhooks(serverId: string | null) {
  return useQuery({
    queryKey: ["webhooks", serverId],
    queryFn: async () => (await apiClient.get<Webhook[]>(`/servers/${serverId}/webhooks`)).data,
    enabled: serverId !== null,
  });
}

export function useCreateWebhook(serverId: string | null) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (url: string) => (await apiClient.post<Webhook>(`/servers/${serverId}/webhooks`, { url })).data,
    onSuccess: async () => queryClient.invalidateQueries({ queryKey: ["webhooks", serverId] }),
  });
}

export function useDeleteWebhook(serverId: string | null) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => apiClient.delete(`/servers/${serverId}/webhooks/${id}`),
    onSuccess: async () => queryClient.invalidateQueries({ queryKey: ["webhooks", serverId] }),
  });
}
