using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rustex.Api.Dtos;
using Rustex.Domain.Entities;
using Rustex.Infrastructure.Persistence;

namespace Rustex.Api.Controllers;

[ApiController]
[Route("api/teams/{teamId:guid}/message-templates")]
[Authorize]
public class MessageTemplatesController : ControllerBase
{
    private readonly AppDbContext _db;

    public MessageTemplatesController(AppDbContext db) => _db = db;

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);

    [HttpGet]
    public async Task<ActionResult<List<MessageTemplateResponse>>> List(Guid teamId, CancellationToken ct)
    {
        if (!await IsTeamMemberAsync(teamId, ct)) return Forbid();

        var templates = await _db.MessageTemplates
            .Where(t => t.TeamId == teamId)
            .OrderBy(t => t.EventType)
            .ToListAsync(ct);

        return templates.Select(ToResponse).ToList();
    }

    [HttpPost]
    public async Task<ActionResult<MessageTemplateResponse>> Create(Guid teamId, [FromBody] CreateMessageTemplateRequest request, CancellationToken ct)
    {
        if (!await IsTeamMemberAsync(teamId, ct)) return Forbid();

        if (string.IsNullOrWhiteSpace(request.TemplateText))
            return BadRequest("Template text is required.");

        var exists = await _db.MessageTemplates.AnyAsync(
            t => t.TeamId == teamId && t.ServerId == request.ServerId && t.EventType == request.EventType, ct);
        if (exists)
            return Conflict("A template for this event type already exists for this server (or for all servers).");

        var template = new MessageTemplate
        {
            TeamId = teamId,
            ServerId = request.ServerId,
            EventType = request.EventType,
            TemplateText = request.TemplateText,
            IsEnabled = request.IsEnabled,
            CooldownSeconds = request.CooldownSeconds,
        };

        _db.MessageTemplates.Add(template);
        await _db.SaveChangesAsync(ct);

        return ToResponse(template);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<MessageTemplateResponse>> Update(Guid teamId, Guid id, [FromBody] UpdateMessageTemplateRequest request, CancellationToken ct)
    {
        if (!await IsTeamMemberAsync(teamId, ct)) return Forbid();

        var template = await _db.MessageTemplates.FirstOrDefaultAsync(t => t.Id == id && t.TeamId == teamId, ct);
        if (template is null) return NotFound();

        if (string.IsNullOrWhiteSpace(request.TemplateText))
            return BadRequest("Template text is required.");

        template.TemplateText = request.TemplateText;
        template.IsEnabled = request.IsEnabled;
        template.CooldownSeconds = request.CooldownSeconds;

        await _db.SaveChangesAsync(ct);
        return ToResponse(template);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid teamId, Guid id, CancellationToken ct)
    {
        if (!await IsTeamMemberAsync(teamId, ct)) return Forbid();

        var template = await _db.MessageTemplates.FirstOrDefaultAsync(t => t.Id == id && t.TeamId == teamId, ct);
        if (template is null) return NotFound();

        _db.MessageTemplates.Remove(template);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<bool> IsTeamMemberAsync(Guid teamId, CancellationToken ct) =>
        await _db.TeamMembers.AnyAsync(m => m.TeamId == teamId && m.UserId == CurrentUserId, ct);

    private static MessageTemplateResponse ToResponse(MessageTemplate t) => new(
        t.Id, t.TeamId, t.ServerId, t.EventType, t.TemplateText, t.IsEnabled, t.CooldownSeconds, t.CreatedAt);
}
