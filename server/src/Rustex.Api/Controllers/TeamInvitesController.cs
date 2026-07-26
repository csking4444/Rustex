using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rustex.Api.Dtos;
using Rustex.Domain;
using Rustex.Domain.Entities;
using Rustex.Infrastructure.Persistence;

namespace Rustex.Api.Controllers;

[ApiController]
[Route("api/teams/{teamId:guid}/invites")]
[Authorize]
public class TeamInvitesController : ControllerBase
{
    private readonly AppDbContext _db;

    public TeamInvitesController(AppDbContext db) => _db = db;

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);

    [HttpGet]
    public async Task<ActionResult<List<TeamInviteResponse>>> List(Guid teamId, CancellationToken ct)
    {
        if (!await IsMemberAsync(teamId, ct)) return Forbid();

        var invites = await _db.TeamInvites
            .Where(i => i.TeamId == teamId && i.Status == InviteStatus.Pending)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(ct);

        return invites.Select(ToResponse).ToList();
    }

    [HttpPost]
    public async Task<ActionResult<TeamInviteResponse>> Create(Guid teamId, [FromBody] CreateTeamInviteRequest request, CancellationToken ct)
    {
        if (!await IsMemberAsync(teamId, ct)) return Forbid();

        var invite = new TeamInvite
        {
            TeamId = teamId,
            InviterId = CurrentUserId,
            InviteeDiscord = request.InviteeDiscord,
            Token = GenerateToken(),
            Status = InviteStatus.Pending,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
        };

        _db.TeamInvites.Add(invite);
        await _db.SaveChangesAsync(ct);

        return ToResponse(invite);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Revoke(Guid teamId, Guid id, CancellationToken ct)
    {
        if (!await IsMemberAsync(teamId, ct)) return Forbid();

        var invite = await _db.TeamInvites.FirstOrDefaultAsync(i => i.Id == id && i.TeamId == teamId, ct);
        if (invite is null) return NotFound();

        invite.Status = InviteStatus.Revoked;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private Task<bool> IsMemberAsync(Guid teamId, CancellationToken ct) =>
        _db.TeamMembers.AnyAsync(m => m.TeamId == teamId && m.UserId == CurrentUserId && m.Status == TeamMemberStatus.Active, ct);

    private static string GenerateToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static TeamInviteResponse ToResponse(TeamInvite i) =>
        new(i.Id, i.Token, i.InviteeDiscord, i.Status, i.ExpiresAt, i.CreatedAt);
}

/// <summary>Separate top-level route (not nested under a team) since accepting an invite is the
/// one action a non-member needs to perform, identified purely by the invite token.</summary>
[ApiController]
[Route("api/team-invites")]
[Authorize]
public class TeamInviteAcceptanceController : ControllerBase
{
    private readonly AppDbContext _db;

    public TeamInviteAcceptanceController(AppDbContext db) => _db = db;

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);

    [HttpPost("{token}/accept")]
    public async Task<ActionResult<TeamResponse>> Accept(string token, CancellationToken ct)
    {
        var invite = await _db.TeamInvites.Include(i => i.Team).FirstOrDefaultAsync(i => i.Token == token, ct);
        if (invite is null) return NotFound();

        if (invite.Status != InviteStatus.Pending) return BadRequest("This invite is no longer valid.");
        if (invite.ExpiresAt < DateTimeOffset.UtcNow)
        {
            invite.Status = InviteStatus.Expired;
            await _db.SaveChangesAsync(ct);
            return BadRequest("This invite has expired.");
        }

        var userId = CurrentUserId;
        var alreadyMember = await _db.TeamMembers.AnyAsync(m => m.TeamId == invite.TeamId && m.UserId == userId, ct);
        if (alreadyMember) return BadRequest("You're already a member of this team.");

        var memberRole = await _db.TeamRoles.FirstOrDefaultAsync(r => r.TeamId == invite.TeamId && r.Name == "Member", ct);
        if (memberRole is null) return BadRequest("This team has no default Member role configured.");

        _db.TeamMembers.Add(new TeamMember
        {
            TeamId = invite.TeamId,
            UserId = userId,
            RoleId = memberRole.Id,
            Status = TeamMemberStatus.Active,
        });

        invite.Status = InviteStatus.Accepted;
        await _db.SaveChangesAsync(ct);

        return new TeamResponse(invite.Team.Id, invite.Team.Name, invite.Team.Slug, invite.Team.IconUrl, invite.Team.CreatedAt, memberRole.Name);
    }
}
