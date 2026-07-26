using Rustex.Domain;

namespace Rustex.Api.Dtos;

public record TeamMemberResponse(
    Guid Id,
    Guid UserId,
    string DiscordUsername,
    string? DiscordAvatar,
    string RoleName,
    TeamMemberStatus Status,
    DateTimeOffset JoinedAt);

public record UpdateMemberRoleRequest(string RoleName);

public record CreateTeamInviteRequest(string? InviteeDiscord);

public record TeamInviteResponse(
    Guid Id,
    string Token,
    string? InviteeDiscord,
    InviteStatus Status,
    DateTimeOffset ExpiresAt,
    DateTimeOffset CreatedAt);
