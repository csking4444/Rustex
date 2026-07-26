using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rustex.Api.Dtos;
using Rustex.Infrastructure.Persistence;

namespace Rustex.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly AppDbContext _db;

    public UsersController(AppDbContext db) => _db = db;

    [HttpGet("me")]
    public async Task<ActionResult<CurrentUserResponse>> Me(CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);

        var user = await _db.Users.Include(u => u.Profile)
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null) return NotFound();

        return new CurrentUserResponse(
            user.Id,
            user.Username,
            user.AvatarUrl,
            user.Email,
            user.Profile?.DisplayName,
            user.Profile?.Timezone ?? "UTC",
            user.PasswordHash is not null,
            user.DiscordId is not null,
            user.SteamId is not null);
    }
}
