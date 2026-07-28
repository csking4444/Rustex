using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Rustex.Api.Auth;
using Rustex.Domain.Abstractions;

namespace Rustex.Api.Hubs;

[Authorize]
public class DashboardHub : Hub
{
    private readonly IClientConnectionRegistry _connectionRegistry;
    private readonly ILiveScopeAuthorizer _scopeAuthorizer;
    private readonly ILiveStateStore _liveState;
    private readonly ILogger<DashboardHub> _log;

    public DashboardHub(
        IClientConnectionRegistry connectionRegistry,
        ILiveScopeAuthorizer scopeAuthorizer,
        ILiveStateStore liveState,
        ILogger<DashboardHub> log)
    {
        _connectionRegistry = connectionRegistry;
        _scopeAuthorizer = scopeAuthorizer;
        _liveState = liveState;
        _log = log;
    }

    public override async Task OnConnectedAsync()
    {
        var userId = CurrentUserId;
        if (userId is not null)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, UserGroupName(userId.Value));

            // Frontend passes ?clientKind=app when running installed/standalone (see
            // client/src/lib/signalr.ts) — anything else is treated as a plain desktop tab.
            var clientKindRaw = Context.GetHttpContext()?.Request.Query["clientKind"].ToString();
            var kind = clientKindRaw == "app" ? ClientKind.App : ClientKind.Desktop;
            _connectionRegistry.Register(Context.ConnectionId, userId.Value, kind);
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _connectionRegistry.Unregister(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>Joins a live scope after checking the caller may actually see it.
    ///
    /// The authorization call is the important part: SignalR adds a connection to whatever group
    /// name it is given, so without it any signed-in user could pass another account's server id
    /// and start receiving their live data.</summary>
    public async Task<SubscribeResult> SubscribeScope(string scope)
    {
        var userId = CurrentUserId;
        if (userId is null) return SubscribeResult.Denied("Not signed in.");

        if (!LiveScope.TryParse(scope, out var parsed))
            return SubscribeResult.Denied("Malformed scope.");

        if (!await _scopeAuthorizer.CanAccessAsync(userId.Value, parsed, Context.ConnectionAborted))
        {
            _log.LogWarning("User {UserId} was denied live scope {Scope}", userId, scope);
            // Same message whether the scope does not exist or belongs to someone else — telling
            // them apart would let a caller enumerate which server ids are real.
            return SubscribeResult.Denied("You do not have access to that scope.");
        }


        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(parsed));

        // Hand back the current snapshot in the same round trip. A client that reconnects is
        // immediately correct rather than showing stale data until the next push — which for the
        // 30s team poll could otherwise be half a minute of wrong information on screen.
        var snapshot = await _liveState.GetSnapshotAsync(parsed, Context.ConnectionAborted);
        return SubscribeResult.Ok(snapshot);
    }

    public async Task UnsubscribeScope(string scope)
    {
        if (LiveScope.TryParse(scope, out var parsed))
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(parsed));
    }

    /// <summary>Re-fetches current state. Clients call this when they notice a version gap in the
    /// updates they received, which means a message was missed and local state cannot be trusted.</summary>
    public async Task<LiveSnapshot?> GetSnapshot(string scope)
    {
        var userId = CurrentUserId;
        if (userId is null) return null;
        if (!LiveScope.TryParse(scope, out var parsed)) return null;
        if (!await _scopeAuthorizer.CanAccessAsync(userId.Value, parsed, Context.ConnectionAborted)) return null;

        return await _liveState.GetSnapshotAsync(parsed, Context.ConnectionAborted);
    }

    private Guid? CurrentUserId
    {
        get
        {
            var raw = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? Context.User?.FindFirstValue("sub");
            return Guid.TryParse(raw, out var id) ? id : null;
        }
    }

    public static string GroupName(LiveScope scope) => scope.ToString();
    public static string ServerGroupName(string serverId) => $"server:{serverId}";
    public static string UserGroupName(Guid userId) => $"user:{userId}";
}

/// <summary>Result of a subscribe attempt. Explicit rather than an exception so the client gets a
/// usable reason and can decide between "upgrade your plan" and "this server is gone".</summary>
public sealed record SubscribeResult(bool Allowed, string? Reason, LiveSnapshot? Snapshot)
{
    public static SubscribeResult Ok(LiveSnapshot? snapshot) => new(true, null, snapshot);
    public static SubscribeResult Denied(string reason) => new(false, reason, null);
}
