using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rustex.Api.Dtos;
using Rustex.Domain.Entities;
using Rustex.Infrastructure.Persistence;

namespace Rustex.Api.Controllers;

[ApiController]
[Route("api/servers/{serverId:guid}/webhooks")]
[Authorize]
public class WebhooksController : ControllerBase
{
    private readonly AppDbContext _db;

    public WebhooksController(AppDbContext db) => _db = db;

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);

    [HttpGet]
    public async Task<ActionResult<List<WebhookResponse>>> List(Guid serverId, CancellationToken ct)
    {
        if (!await OwnsServerAsync(serverId, ct)) return NotFound();

        var webhooks = await _db.Webhooks.Where(w => w.ServerId == serverId).ToListAsync(ct);
        return webhooks.Select(ToResponse).ToList();
    }

    [HttpPost]
    public async Task<ActionResult<WebhookResponse>> Create(Guid serverId, [FromBody] CreateWebhookRequest request, CancellationToken ct)
    {
        if (!await OwnsServerAsync(serverId, ct)) return NotFound();

        if (string.IsNullOrWhiteSpace(request.Url) ||
            !Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps))
        {
            return BadRequest("A valid https:// webhook URL is required.");
        }

        var webhook = new Webhook
        {
            ServerId = serverId,
            Url = request.Url,
            Secret = "", // not used for Discord webhooks — the URL itself is the credential
            EventTypes = request.EventTypes is { Count: > 0 } ? request.EventTypes : new List<string> { "RaidDetected" },
            IsActive = true,
        };

        _db.Webhooks.Add(webhook);
        await _db.SaveChangesAsync(ct);
        return ToResponse(webhook);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid serverId, Guid id, CancellationToken ct)
    {
        if (!await OwnsServerAsync(serverId, ct)) return NotFound();

        var webhook = await _db.Webhooks.FirstOrDefaultAsync(w => w.Id == id && w.ServerId == serverId, ct);
        if (webhook is null) return NotFound();

        _db.Webhooks.Remove(webhook);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private Task<bool> OwnsServerAsync(Guid serverId, CancellationToken ct) =>
        _db.RustServers.AnyAsync(s => s.Id == serverId && s.OwnerUserId == CurrentUserId, ct);

    private static WebhookResponse ToResponse(Webhook w) => new(w.Id, w.ServerId, w.Url, w.EventTypes, w.IsActive, w.CreatedAt);
}
