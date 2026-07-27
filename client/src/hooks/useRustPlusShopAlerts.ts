import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "@/lib/apiClient";
import type { ShopAlert } from "@/types";

export interface ShopAlertInput {
  itemId: number | null;
  itemNameContains: string | null;
  maxCostPerItem: number | null;
  minAmountInStock: number;
  notifyOnNewListing: boolean;
  notifyOnPriceDrop: boolean;
  notifyOnRestock: boolean;
  cooldownSeconds: number;
}

export function useRustPlusShopAlerts(serverId: string | null) {
  return useQuery({
    queryKey: ["rustplus", "shop-alerts", serverId],
    queryFn: async () => (await apiClient.get<ShopAlert[]>(`/servers/${serverId}/rustplus/shop-alerts`)).data,
    enabled: serverId !== null,
  });
}

export function useCreateShopAlert(serverId: string | null) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (input: ShopAlertInput) =>
      (await apiClient.post<ShopAlert>(`/servers/${serverId}/rustplus/shop-alerts`, input)).data,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["rustplus", "shop-alerts", serverId] });
    },
  });
}

export function useUpdateShopAlert(serverId: string | null) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, ...input }: ShopAlertInput & { id: string; isEnabled: boolean }) =>
      (await apiClient.put<ShopAlert>(`/servers/${serverId}/rustplus/shop-alerts/${id}`, input)).data,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["rustplus", "shop-alerts", serverId] });
    },
  });
}

export function useDeleteShopAlert(serverId: string | null) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => apiClient.delete(`/servers/${serverId}/rustplus/shop-alerts/${id}`),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["rustplus", "shop-alerts", serverId] });
    },
  });
}
