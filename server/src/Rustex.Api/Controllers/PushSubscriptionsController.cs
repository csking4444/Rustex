using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rustex.Api.Dtos;
using Rustex.Domain.Abstractions;
using Rustex.Domain.Entities;
using Rustex.Infrastructure.Persistence;

namespace Rustex.Api.Controllers;

[ApiController]
[Route("api/push")]
[Authorize]
public class PushSubscriptionsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IWebPushSender _webPushSender;

    public PushSubscriptionsController(AppDbContext db, IWebPushSender webPushSender)
    {
        _db = db;
        _webPushSender = webPushSender;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);

    [HttpGet("vapid-public-key")]
    public ActionResult<VapidPublicKeyResponse> VapidPublicKey() =>
        new VapidPublicKeyResponse(_webPushSender.IsConfigured ? _webPushSender.PublicKey : null);

    [HttpPost("subscriptions")]
    public async Task<IActionResult> Subscribe([FromBody] SubscribeRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId;

        var existing = await _db.PushSubscriptions.FirstOrDefaultAsync(s => s.Endpoint == request.Endpoint, ct);
        if (existing is not null)
        {
            // The same browser endpoint re-subscribing (e.g. after a key rotation) — keep one
            // row per endpoint and just reassign it to the current user.
            existing.UserId = userId;
            existing.P256dhKey = request.P256dhKey;
            existing.AuthKey = request.AuthKey;
        }
        else
        {
            _db.PushSubscriptions.Add(new PushSubscription
            {
                UserId = userId,
                Endpoint = request.Endpoint,
                P256dhKey = request.P256dhKey,
                AuthKey = request.AuthKey,
            });
        }

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("unsubscribe")]
    public async Task<IActionResult> Unsubscribe([FromBody] UnsubscribeRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId;
        var subscription = await _db.PushSubscriptions.FirstOrDefaultAsync(s => s.Endpoint == request.Endpoint && s.UserId == userId, ct);
        if (subscription is null) return NoContent();

        _db.PushSubscriptions.Remove(subscription);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}
