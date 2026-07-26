import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "@/lib/apiClient";
import type { UserSettings } from "@/types";

export function useUserSettings() {
  return useQuery({
    queryKey: ["user-settings"],
    queryFn: async () => (await apiClient.get<UserSettings>("/users/me/settings")).data,
  });
}

export function useUpdateUserSettings() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (settings: Omit<UserSettings, "updatedAt">) =>
      (await apiClient.put<UserSettings>("/users/me/settings", settings)).data,
    onSuccess: async () => queryClient.invalidateQueries({ queryKey: ["user-settings"] }),
  });
}
