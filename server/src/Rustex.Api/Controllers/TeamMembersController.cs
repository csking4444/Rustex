using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rustex.Api.Dtos;
using Rustex.Domain;
using Rustex.Infrastructure.Persistence;

namespace Rustex.Api.Controllers;

[ApiController]
[Route("api/teams/{teamId:guid}/members")]
[Authorize]
public class TeamMembersController : ControllerBase
{
    private readonly AppDbContext _db;

    public TeamMembersController(AppDbContext db) => _db = db;

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);

    [HttpGet]
    public async Task<ActionResult<List<TeamMemberResponse>>> List(Guid teamId, CancellationToken ct)
    {
        if (!await IsMemberAsync(teamId, ct)) return Forbid();

        var members = await _db.TeamMembers
            .Include(m => m.User)
            .Include(m => m.Role)
            .Where(m => m.TeamId == teamId && m.Status == TeamMemberStatus.Active)
            .OrderBy(m => m.JoinedAt)
            .ToListAsync(ct);

        return members.Select(m => new TeamMemberResponse(
            m.Id, m.UserId, m.User.DiscordUsername, m.User.DiscordAvatar, m.Role.Name, m.Status, m.JoinedAt)).ToList();
    }

    [HttpPut("{userId:guid}/role")]
    public async Task<ActionResult<TeamMemberResponse>> UpdateRole(Guid teamId, Guid userId, [FromBody] UpdateMemberRoleRequest request, CancellationToken ct)
    {
        if (!await IsOwnerAsync(teamId, ct)) return Forbid();
        if (userId == CurrentUserId) return BadRequest("Owners cannot change their own role.");

        var member = await _db.TeamMembers.Include(m => m.User).Include(m => m.Role)
            .FirstOrDefaultAsync(m => m.TeamId == teamId && m.UserId == userId, ct);
        if (member is null) return NotFound();

        var newRole = await _db.TeamRoles.FirstOrDefaultAsync(r => r.TeamId == teamId && r.Name == request.RoleName, ct);
        if (newRole is null || newRole.Name == "Owner") return BadRequest("Invalid role.");

        member.Role = newRole;
        member.RoleId = newRole.Id;
        await _db.SaveChangesAsync(ct);

        return new TeamMemberResponse(member.Id, member.UserId, member.User.DiscordUsername, member.User.DiscordAvatar, newRole.Name, member.Status, member.JoinedAt);
    }

    [HttpDelete("{userId:guid}")]
    public async Task<IActionResult> Remove(Guid teamId, Guid userId, CancellationToken ct)
    {
        if (!await IsOwnerAsync(teamId, ct) && userId != CurrentUserId) return Forbid();

        var member = await _db.TeamMembers.Include(m => m.Role)
            .FirstOrDefaultAsync(m => m.TeamId == teamId && m.UserId == userId, ct);
        if (member is null) return NotFound();

        if (member.Role.Name == "Owner") return BadRequest("The team owner cannot be removed. Transfer ownership first.");

        _db.TeamMembers.Remove(member);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private Task<bool> IsMemberAsync(Guid teamId, CancellationToken ct) =>
        _db.TeamMembers.AnyAsync(m => m.TeamId == teamId && m.UserId == CurrentUserId && m.Status == TeamMemberStatus.Active, ct);

    private async Task<bool> IsOwnerAsync(Guid teamId, CancellationToken ct)
    {
        var membership = await _db.TeamMembers.Include(m => m.Role)
            .FirstOrDefaultAsync(m => m.TeamId == teamId && m.UserId == CurrentUserId, ct);
        return membership?.Role.Name == "Owner";
    }
}
