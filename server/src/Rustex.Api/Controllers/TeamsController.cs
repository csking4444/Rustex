using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rustex.Api.Dtos;
using Rustex.Domain;
using Rustex.Domain.Entities;
using Rustex.Infrastructure.Persistence;

namespace Rustex.Api.Controllers;

[ApiController]
[Route("api/teams")]
[Authorize]
public class TeamsController : ControllerBase
{
    private readonly AppDbContext _db;

    public TeamsController(AppDbContext db) => _db = db;

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);

    [HttpGet]
    public async Task<ActionResult<List<TeamResponse>>> List(CancellationToken ct)
    {
        var userId = CurrentUserId;

        var memberships = await _db.TeamMembers
            .Include(m => m.Team)
            .Include(m => m.Role)
            .Where(m => m.UserId == userId && m.Status == TeamMemberStatus.Active)
            .ToListAsync(ct);

        return memberships
            .Select(m => new TeamResponse(m.Team.Id, m.Team.Name, m.Team.Slug, m.Team.IconUrl, m.Team.CreatedAt, m.Role.Name))
            .ToList();
    }

    [HttpPost]
    public async Task<ActionResult<TeamResponse>> Create([FromBody] CreateTeamRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId;
        var slug = Slugify(request.Name);

        if (await _db.Teams.AnyAsync(t => t.Slug == slug, ct))
            slug = $"{slug}-{Guid.NewGuid().ToString("N")[..6]}";

        var team = new Team { Name = request.Name, Slug = slug, OwnerId = userId };
        _db.Teams.Add(team);

        var ownerRole = new TeamRole { Team = team, TeamId = team.Id, Name = "Owner", IsSystem = true };
        var adminRole = new TeamRole { Team = team, TeamId = team.Id, Name = "Admin", IsSystem = true };
        var memberRole = new TeamRole { Team = team, TeamId = team.Id, Name = "Member", IsSystem = true };
        _db.TeamRoles.AddRange(ownerRole, adminRole, memberRole);

        _db.TeamMembers.Add(new TeamMember
        {
            Team = team,
            TeamId = team.Id,
            UserId = userId,
            Role = ownerRole,
            RoleId = ownerRole.Id,
            Status = TeamMemberStatus.Active,
        });

        await _db.SaveChangesAsync(ct);

        return new TeamResponse(team.Id, team.Name, team.Slug, team.IconUrl, team.CreatedAt, ownerRole.Name);
    }

    private static string Slugify(string input) =>
        new string(input.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray())
            .Trim('-');
}
