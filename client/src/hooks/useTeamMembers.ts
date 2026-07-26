import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "@/lib/apiClient";
import type { TeamInviteSummary, TeamMemberSummary } from "@/types";

export function useTeamMembers(teamId: string | null) {
  return useQuery({
    queryKey: ["team-members", teamId],
    queryFn: async () => (await apiClient.get<TeamMemberSummary[]>(`/teams/${teamId}/members`)).data,
    enabled: teamId !== null,
  });
}

export function useTeamInvites(teamId: string | null) {
  return useQuery({
    queryKey: ["team-invites", teamId],
    queryFn: async () => (await apiClient.get<TeamInviteSummary[]>(`/teams/${teamId}/invites`)).data,
    enabled: teamId !== null,
  });
}

export function useCreateTeamInvite(teamId: string | null) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (inviteeDiscord: string) =>
      (await apiClient.post<TeamInviteSummary>(`/teams/${teamId}/invites`, { inviteeDiscord: inviteeDiscord || null })).data,
    onSuccess: async () => queryClient.invalidateQueries({ queryKey: ["team-invites", teamId] }),
  });
}

export function useRevokeTeamInvite(teamId: string | null) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => apiClient.delete(`/teams/${teamId}/invites/${id}`),
    onSuccess: async () => queryClient.invalidateQueries({ queryKey: ["team-invites", teamId] }),
  });
}

export function useRemoveTeamMember(teamId: string | null) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (userId: string) => apiClient.delete(`/teams/${teamId}/members/${userId}`),
    onSuccess: async () => queryClient.invalidateQueries({ queryKey: ["team-members", teamId] }),
  });
}

export function useUpdateMemberRole(teamId: string | null) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (vars: { userId: string; roleName: string }) =>
      (await apiClient.put<TeamMemberSummary>(`/teams/${teamId}/members/${vars.userId}/role`, { roleName: vars.roleName })).data,
    onSuccess: async () => queryClient.invalidateQueries({ queryKey: ["team-members", teamId] }),
  });
}
