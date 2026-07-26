import { useState, type FormEvent } from "react";
import { Copy, Mail, Trash2, UserMinus, Users } from "lucide-react";
import { Card, CardHeader } from "@/components/ui/Card";
import { SkeletonList } from "@/components/ui/Skeleton";
import { useAuth } from "@/context/AuthContext";
import {
  useCreateTeamInvite,
  useRemoveTeamMember,
  useRevokeTeamInvite,
  useTeamInvites,
  useTeamMembers,
  useUpdateMemberRole,
} from "@/hooks/useTeamMembers";

export function TeamMembersPanel({ teamId }: { teamId: string }) {
  const { user } = useAuth();
  const { data: members, isLoading } = useTeamMembers(teamId);
  const { data: invites } = useTeamInvites(teamId);

  const createInvite = useCreateTeamInvite(teamId);
  const revokeInvite = useRevokeTeamInvite(teamId);
  const removeMember = useRemoveTeamMember(teamId);
  const updateRole = useUpdateMemberRole(teamId);

  const [inviteeDiscord, setInviteeDiscord] = useState("");
  const currentMembership = members?.find((m) => m.userId === user?.id);
  const isOwner = currentMembership?.roleName === "Owner";

  function handleInvite(e: FormEvent) {
    e.preventDefault();
    createInvite.mutate(inviteeDiscord, { onSuccess: () => setInviteeDiscord("") });
  }

  function copyInviteLink(token: string) {
    void navigator.clipboard.writeText(`${window.location.origin}/teams/invite/${token}`);
  }

  return (
    <Card>
      <CardHeader title="Members & Invites" subtitle={members ? `${members.length} member${members.length === 1 ? "" : "s"}` : undefined} />

      <form onSubmit={handleInvite} className="mb-4 flex gap-3">
        <input
          value={inviteeDiscord}
          onChange={(e) => setInviteeDiscord(e.target.value)}
          placeholder="Discord username (optional)"
          className="flex-1 rounded-xl border border-white/10 bg-base-800/60 px-3 py-2 text-sm text-text-primary placeholder:text-text-muted focus:outline-none focus:ring-2 focus:ring-blood-light/60"
        />
        <button type="submit" disabled={createInvite.isPending} className="btn-primary">
          <Mail className="h-4 w-4" />
          Invite
        </button>
      </form>

      {invites && invites.length > 0 && (
        <div className="mb-4 flex flex-col gap-2">
          <p className="text-xs font-medium text-text-muted">Pending invites</p>
          {invites.map((invite) => (
            <div key={invite.id} className="flex items-center justify-between rounded-lg border border-white/5 bg-base-800/40 px-3 py-2">
              <span className="text-xs text-text-secondary">{invite.inviteeDiscord ?? "Anyone with the link"}</span>
              <div className="flex items-center gap-2">
                <button onClick={() => copyInviteLink(invite.token)} className="text-text-muted hover:text-white" aria-label="Copy invite link">
                  <Copy className="h-3.5 w-3.5" />
                </button>
                <button onClick={() => revokeInvite.mutate(invite.id)} className="text-text-muted hover:text-critical" aria-label="Revoke invite">
                  <Trash2 className="h-3.5 w-3.5" />
                </button>
              </div>
            </div>
          ))}
        </div>
      )}

      {isLoading && <SkeletonList rows={3} />}

      {!isLoading && members?.length === 0 && (
        <div className="flex flex-col items-center justify-center gap-2 py-8 text-text-muted">
          <Users className="h-8 w-8" />
          <p className="text-sm">No members yet.</p>
        </div>
      )}

      {!isLoading && members && members.length > 0 && (
        <ul className="flex flex-col gap-2">
          {members.map((member) => (
            <li key={member.id} className="flex items-center justify-between rounded-xl border border-white/5 bg-base-800/40 px-4 py-3">
              <div className="flex items-center gap-3">
                <div className="flex h-8 w-8 items-center justify-center rounded-full bg-base-700 text-xs font-semibold text-text-secondary">
                  {member.discordUsername.slice(0, 2).toUpperCase()}
                </div>
                <p className="text-sm font-medium text-text-primary">{member.discordUsername}</p>
              </div>

              <div className="flex items-center gap-3">
                {isOwner && member.roleName !== "Owner" ? (
                  <select
                    value={member.roleName}
                    onChange={(e) => updateRole.mutate({ userId: member.userId, roleName: e.target.value })}
                    className="rounded-lg border border-white/10 bg-base-800/60 px-2 py-1 text-xs text-text-primary focus:outline-none"
                  >
                    <option value="Admin">Admin</option>
                    <option value="Member">Member</option>
                  </select>
                ) : (
                  <span className="badge-info">{member.roleName}</span>
                )}

                {(isOwner || member.userId === user?.id) && member.roleName !== "Owner" && (
                  <button
                    onClick={() => removeMember.mutate(member.userId)}
                    className="text-text-muted transition-colors hover:text-critical"
                    aria-label={member.userId === user?.id ? "Leave team" : "Remove member"}
                  >
                    <UserMinus className="h-4 w-4" />
                  </button>
                )}
              </div>
            </li>
          ))}
        </ul>
      )}
    </Card>
  );
}
