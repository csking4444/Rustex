import { useQuery } from "@tanstack/react-query";
import { apiClient } from "@/lib/apiClient";
import type { RustServerSummary } from "@/types";

export function useServers() {
  return useQuery({
    queryKey: ["servers"],
    queryFn: async () => (await apiClient.get<RustServerSummary[]>("/servers")).data,
  });
}
