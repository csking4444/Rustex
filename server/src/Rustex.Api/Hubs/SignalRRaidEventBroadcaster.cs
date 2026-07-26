using Microsoft.AspNetCore.SignalR;
using Rustex.Domain.Abstractions;

namespace Rustex.Api.Hubs;

public class SignalRRaidEventBroadcaster : IRaidEventBroadcaster
{
    private readonly IHubContext<DashboardHub> _hub;

    public SignalRRaidEventBroadcaster(IHubContext<DashboardHub> hub) => _hub = hub;

    public Task BroadcastRaidEventCreatedAsync(Guid serverId, object payload, CancellationToken ct) =>
        _hub.Clients.Group(DashboardHub.ServerGroupName(serverId.ToString())).SendAsync("RaidEventCreated", payload, ct);

    public Task BroadcastServerStatusUpdatedAsync(Guid serverId, object payload, CancellationToken ct) =>
        _hub.Clients.Group(DashboardHub.ServerGroupName(serverId.ToString())).SendAsync("ServerStatusUpdated", payload, ct);

    public Task BroadcastIncomingRaidCallAsync(Guid userId, object payload, CancellationToken ct) =>
        _hub.Clients.Group(DashboardHub.UserGroupName(userId)).SendAsync("IncomingRaidCall", payload, ct);

    public Task BroadcastRaidAlertNotificationAsync(Guid userId, object payload, CancellationToken ct) =>
        _hub.Clients.Group(DashboardHub.UserGroupName(userId)).SendAsync("RaidAlertNotification", payload, ct);
}
