import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "@/lib/apiClient";
import type { MapInfo, MapMarker, MarkerType } from "@/types";

export function useServerMap(serverId: string | null) {
  return useQuery({
    queryKey: ["map", serverId],
    queryFn: async () => (await apiClient.get<MapInfo>(`/servers/${serverId}/map`)).data,
    enabled: serverId !== null,
  });
}

export function useMarkers(serverId: string | null) {
  return useQuery({
    queryKey: ["markers", serverId],
    queryFn: async () => (await apiClient.get<MapMarker[]>(`/servers/${serverId}/map/markers`)).data,
    enabled: serverId !== null,
  });
}

export function useCreateMarker(serverId: string | null) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (body: { type: MarkerType; x: number; y: number; label: string | null; color: string | null; isShared: boolean }) =>
      (await apiClient.post<MapMarker>(`/servers/${serverId}/map/markers`, body)).data,
    onSuccess: async () => queryClient.invalidateQueries({ queryKey: ["markers", serverId] }),
  });
}

export function useDeleteMarker(serverId: string | null) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => apiClient.delete(`/servers/${serverId}/map/markers/${id}`),
    onSuccess: async () => queryClient.invalidateQueries({ queryKey: ["markers", serverId] }),
  });
}
