import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "@/lib/apiClient";
import type { RustPlusCredentialStatus, RustPlusLinkCode } from "@/types";

export function useRustPlusCredentialStatus() {
  return useQuery({
    queryKey: ["rustplus", "credentials", "status"],
    queryFn: async () => (await apiClient.get<RustPlusCredentialStatus>("/rustplus/credentials/status")).data,
  });
}

export function useCreateRustPlusLinkCode() {
  return useMutation({
    mutationFn: async () => (await apiClient.post<RustPlusLinkCode>("/rustplus/link-codes")).data,
  });
}

export function useDeleteRustPlusCredentials() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async () => apiClient.delete("/rustplus/credentials"),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["rustplus", "credentials", "status"] });
    },
  });
}
