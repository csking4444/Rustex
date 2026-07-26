import { useState, type FormEvent } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Plus, Users } from "lucide-react";
import { apiClient } from "@/lib/apiClient";
import { useTeams } from "@/hooks/useTeams";
import { Card, CardHeader } from "@/components/ui/Card";
import { SkeletonList } from "@/components/ui/Skeleton";
import { MessageTemplatesPanel } from "@/components/teams/MessageTemplatesPanel";
import { TeamMembersPanel } from "@/components/teams/TeamMembersPanel";

export default function TeamsPage() {
  const { data: teams, isLoading } = useTeams();
  const queryClient = useQueryClient();
  const [name, setName] = useState("");
  const [selectedTeamId, setSelectedTeamId] = useState<string | null>(null);

  const createTeam = useMutation({
    mutationFn: async () => apiClient.post("/teams", { name }),
    onSuccess: async () => {
      setName("");
      await queryClient.invalidateQueries({ queryKey: ["teams"] });
    },
  });

  function handleSubmit(e: FormEvent) {
    e.preventDefault();
    if (name.trim()) createTeam.mutate();
  }

  return (
    <div className="flex flex-col gap-6">
      <div>
        <h1 className="text-2xl font-semibold text-text-primary">Teams</h1>
        <p className="mt-1 text-sm text-text-muted">Invites, roles, chat automation templates, and shared alerts.</p>
      </div>

      <form onSubmit={handleSubmit} className="flex gap-3">
        <input
          value={name}
          onChange={(e) => setName(e.target.value)}
          placeholder="New team name"
          className="flex-1 rounded-xl border border-white/10 bg-base-800/60 px-3 py-2 text-sm text-text-primary placeholder:text-text-muted focus:outline-none focus:ring-2 focus:ring-blood-light/60"
        />
        <button type="submit" disabled={createTeam.isPending} className="btn-primary">
          <Plus className="h-4 w-4" />
          Create Team
        </button>
      </form>

      <Card>
        <CardHeader title="Your Teams" subtitle={teams ? `${teams.length} total — click one to manage it` : undefined} />

        {isLoading && <SkeletonList rows={3} />}

        {!isLoading && teams?.length === 0 && (
          <div className="flex flex-col items-center justify-center gap-2 py-12 text-text-muted">
            <Users className="h-8 w-8" />
            <p className="text-sm">You're not on any teams yet.</p>
          </div>
        )}

        {!isLoading && teams && teams.length > 0 && (
          <ul className="flex flex-col gap-2">
            {teams.map((team) => (
              <li key={team.id}>
                <button
                  onClick={() => setSelectedTeamId(team.id === selectedTeamId ? null : team.id)}
                  className={`flex w-full items-center justify-between rounded-xl border px-4 py-3 text-left transition-colors ${
                    team.id === selectedTeamId
                      ? "border-blood/40 bg-blood/10"
                      : "border-white/5 bg-base-800/40 hover:border-white/10"
                  }`}
                >
                  <div>
                    <p className="text-sm font-medium text-text-primary">{team.name}</p>
                    <p className="text-xs text-text-muted">/{team.slug}</p>
                  </div>
                  <span className="badge-info">{team.roleName}</span>
                </button>
              </li>
            ))}
          </ul>
        )}
      </Card>

      {selectedTeamId && (
        <div className="grid grid-cols-1 gap-6 xl:grid-cols-2">
          <TeamMembersPanel teamId={selectedTeamId} />
          <MessageTemplatesPanel teamId={selectedTeamId} />
        </div>
      )}
    </div>
  );
}
