using Microsoft.AspNetCore.SignalR;
using Rustex.Domain.Abstractions;

namespace Rustex.Api.Hubs;

/// <summary>Pushes live updates to the group for one scope.
///
/// Group membership is the access control here — a connection only ever lands in a scope's group
/// after <see cref="DashboardHub.SubscribeScope"/> has checked ownership, so this class can send
/// without re-authorizing per message.</summary>
public sealed class SignalRLiveBroadcaster : ILiveBroadcaster
{
    private readonly IHubContext<DashboardHub> _hub;

    public SignalRLiveBroadcaster(IHubContext<DashboardHub> hub) => _hub = hub;

    public Task BroadcastAsync(LiveScope scope, LiveUpdate update, CancellationToken ct) =>
        _hub.Clients.Group(DashboardHub.GroupName(scope)).SendAsync("LiveUpdate", update, ct);
}
