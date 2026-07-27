using Microsoft.EntityFrameworkCore;
using Rustex.Domain.Abstractions;
using Rustex.Infrastructure.Persistence;

namespace Rustex.Api.Auth;

/// <summary>Decides whether a user may listen to a live scope.
///
/// This exists because hub group membership is otherwise unguarded: SignalR will happily add any
/// connection to any group name it is handed, so without a check here a signed-in user could
/// subscribe to another account's server id and receive their live player positions, team roster
/// and raid alerts. Every path that joins a group must go through this.</summary>
public interface ILiveScopeAuthorizer
{
    Task<bool> CanAccessAsync(Guid userId, LiveScope scope, CancellationToken ct);
}

public sealed class LiveScopeAuthorizer : ILiveScopeAuthorizer
{
    private readonly AppDbContext _db;

    public LiveScopeAuthorizer(AppDbContext db) => _db = db;

    public async Task<bool> CanAccessAsync(Guid userId, LiveScope scope, CancellationToken ct) => scope.Kind switch
    {
        // Your own user stream, and nobody else's.
        LiveScope.UserKind => scope.Id == userId,

        // Servers are owned outright, matching how ServersController scopes every read.
        LiveScope.ServerKind => await _db.RustServers
            .AsNoTracking()
            .AnyAsync(s => s.Id == scope.Id && s.OwnerUserId == userId, ct),

        // Unknown scope kinds are refused rather than ignored: a new scope type must opt in here
        // deliberately, not inherit access by default.
        _ => false,
    };
}
