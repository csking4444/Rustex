using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rustex.Api.Dtos;
using Rustex.Domain.Entities;
using Rustex.Infrastructure.Persistence;

namespace Rustex.Api.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly AppDbContext _db;

    public NotificationsController(AppDbContext db) => _db = db;

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);

    [HttpGet]
    public async Task<ActionResult<List<NotificationResponse>>> List([FromQuery] int limit = 20, [FromQuery] bool unreadOnly = false, CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 100);
        var userId = CurrentUserId;

        var query = _db.Notifications.Where(n => n.UserId == userId);
        if (unreadOnly) query = query.Where(n => !n.IsRead);

        var notifications = await query
            .OrderByDescending(n => n.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);

        return notifications.Select(ToResponse).ToList();
    }

    [HttpGet("unread-count")]
    public async Task<ActionResult<int>> UnreadCount(CancellationToken ct)
    {
        var userId = CurrentUserId;
        return await _db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead, ct);
    }

    [HttpPut("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        var userId = CurrentUserId;
        var notification = await _db.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId, ct);
        if (notification is null) return NotFound();

        notification.IsRead = true;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPut("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        var userId = CurrentUserId;
        var unread = await _db.Notifications.Where(n => n.UserId == userId && !n.IsRead).ToListAsync(ct);
        foreach (var n in unread) n.IsRead = true;

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static NotificationResponse ToResponse(Notification n) => new(
        n.Id, n.Type, n.Title, n.Body, n.Severity, n.IsRead, n.RelatedEntityType, n.RelatedEntityId, n.CreatedAt);
}
