using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Rustex.Domain.Abstractions;

namespace Rustex.Api.Hubs;

[Authorize]
public class DashboardHub : Hub
{
    private readonly IClientConnectionRegistry _connectionRegistry;

    public DashboardHub(IClientConnectionRegistry connectionRegistry) => _connectionRegistry = connectionRegistry;

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

    public async Task Subscribe(string serverId) =>
        await Groups.AddToGroupAsync(Context.ConnectionId, ServerGroupName(serverId));

    public async Task Unsubscribe(string serverId) =>
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, ServerGroupName(serverId));

    private Guid? CurrentUserId
    {
        get
        {
            var raw = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? Context.User?.FindFirstValue("sub");
            return Guid.TryParse(raw, out var id) ? id : null;
        }
    }

    public static string ServerGroupName(string serverId) => $"server:{serverId}";
    public static string UserGroupName(Guid userId) => $"user:{userId}";
}
