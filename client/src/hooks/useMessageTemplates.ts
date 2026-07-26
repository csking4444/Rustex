import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "@/lib/apiClient";
import type { ChatTemplateMetadata, MessageTemplate } from "@/types";

export function useMessageTemplates(teamId: string | null) {
  return useQuery({
    queryKey: ["message-templates", teamId],
    queryFn: async () => (await apiClient.get<MessageTemplate[]>(`/teams/${teamId}/message-templates`)).data,
    enabled: teamId !== null,
  });
}

export function useChatTemplateMetadata() {
  return useQuery({
    queryKey: ["chat-template-metadata"],
    queryFn: async () => (await apiClient.get<ChatTemplateMetadata>("/chat-templates/metadata")).data,
    staleTime: Infinity,
  });
}

export function useCreateMessageTemplate(teamId: string | null) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (body: { eventType: string; templateText: string; isEnabled: boolean; cooldownSeconds: number; serverId: string | null }) =>
      (await apiClient.post<MessageTemplate>(`/teams/${teamId}/message-templates`, body)).data,
    onSuccess: async () => queryClient.invalidateQueries({ queryKey: ["message-templates", teamId] }),
  });
}

export function useUpdateMessageTemplate(teamId: string | null) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (vars: { id: string; templateText: string; isEnabled: boolean; cooldownSeconds: number }) =>
      (await apiClient.put<MessageTemplate>(`/teams/${teamId}/message-templates/${vars.id}`, vars)).data,
    onSuccess: async () => queryClient.invalidateQueries({ queryKey: ["message-templates", teamId] }),
  });
}

export function useDeleteMessageTemplate(teamId: string | null) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => apiClient.delete(`/teams/${teamId}/message-templates/${id}`),
    onSuccess: async () => queryClient.invalidateQueries({ queryKey: ["message-templates", teamId] }),
  });
}

export function usePreviewTemplate() {
  return useMutation({
    mutationFn: async (body: { templateText: string; eventType: string | null }) =>
      (await apiClient.post<{ rendered: string }>("/chat-templates/preview", body)).data,
  });
}
