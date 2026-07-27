using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rustex.Domain;
using Rustex.Domain.Abstractions;
using Rustex.Domain.Entities;
using Rustex.Domain.RustPlus;
using Rustex.Infrastructure.Persistence;
using Rustex.Infrastructure.RustPlus.Proto;

namespace Rustex.Infrastructure.RustPlus;

/// <summary>
/// Keeps RustPlusTeamMemberState in sync with each paired server's live team roster and turns
/// online/offline/death/revive transitions into notifications. Two feeds populate the same work
/// queue: the teamChanged broadcast (near-instant, but only arrives while a session's socket is
/// alive and subscribed) and a 30s fallback poll (covers the gap right after a (re)connect and
/// any session where setSubscription silently failed). Both hand raw AppTeamInfo payloads to a
/// single channel consumer so DB work never happens on the WebSocket receive thread.
/// </summary>
public class RustPlusTeamTrackingWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);
    private const int DefaultMapSize = 4000; // used only when RustServer.WorldSize hasn't been recorded yet

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RustPlusConnectionManager _connectionManager;
    private readonly ILogger<RustPlusTeamTrackingWorker> _logger;
    private readonly Channel<(Guid PairingId, AppTeamInfo TeamInfo)> _channel =
        Channel.CreateBounded<(Guid, AppTeamInfo)>(new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.DropOldest });

    public RustPlusTeamTrackingWorker(IServiceScopeFactory scopeFactory, RustPlusConnectionManager connectionManager, ILogger<RustPlusTeamTrackingWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _connectionManager = connectionManager;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _connectionManager.OnBroadcast += OnBroadcast;
        stoppingToken.Register(() => _connectionManager.OnBroadcast -= OnBroadcast);

        return Task.WhenAll(
            ConsumeAsync(stoppingToken),
            PollLoopAsync(stoppingToken));
    }

    private void OnBroadcast(Guid pairingId, AppBroadcast broadcast)
    {
        if (broadcast.TeamChanged?.TeamInfo is { } teamInfo)
            _channel.Writer.TryWrite((pairingId, teamInfo));
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(PollInterval);
        do
        {
            try
            {
                await PollOnceAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RustPlusTeamTrackingWorker poll tick failed");
            }
        } while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
    }

    private async Task PollOnceAsync(CancellationToken ct)
    {
        foreach (var pairingId in _connectionManager.ActiveSessionIds)
        {
            if (!_connectionManager.TryGetClient(pairingId, out var client) || client is null)
                continue; // mid-reconnect — skip this tick, the broadcast feed or next poll will catch up

            try
            {
                var teamInfo = await client.GetTeamInfoAsync(ct);
                _channel.Writer.TryWrite((pairingId, teamInfo));
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Fallback getTeamInfo poll failed for pairing {PairingId}", pairingId);
            }
        }
    }

    private async Task ConsumeAsync(CancellationToken ct)
    {
        await foreach (var (pairingId, teamInfo) in _channel.Reader.ReadAllAsync(ct))
        {
            try
            {
                await ProcessAsync(pairingId, teamInfo, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process team info for pairing {PairingId}", pairingId);
            }
        }
    }

    private async Task ProcessAsync(Guid pairingId, AppTeamInfo teamInfo, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();

        var pairing = await db.RustPlusPairings.Include(p => p.Server)
            .FirstOrDefaultAsync(p => p.Id == pairingId, ct);
        if (pairing is null) return; // pairing was deleted after this tick was queued

        var mapSize = pairing.Server.WorldSize ?? DefaultMapSize;
        var existingStates = await db.RustPlusTeamMemberStates
            .Where(s => s.ServerId == pairing.ServerId)
            .ToDictionaryAsync(s => s.SteamId, ct);

        foreach (var member in teamInfo.Members)
        {
            var grid = GridConverter.ToGrid(member.X, member.Y, mapSize);
            var now = DateTimeOffset.UtcNow;

            if (!existingStates.TryGetValue(member.SteamId, out var state))
            {
                state = new RustPlusTeamMemberState { ServerId = pairing.ServerId, SteamId = member.SteamId };
                db.RustPlusTeamMemberStates.Add(state);
                existingStates[member.SteamId] = state;

                state.Name = member.Name;
                state.IsOnline = member.IsOnline;
                state.IsAlive = member.IsAlive;
                state.LastX = member.X;
                state.LastY = member.Y;
                state.LastGrid = grid;
                state.LastSeenAt = now;
                state.UpdatedAt = now;
                continue; // no prior state to diff against — don't fire a transition on first sight
            }

            var wasOnline = state.IsOnline;
            var wasAlive = state.IsAlive;

            state.Name = member.Name;
            state.IsOnline = member.IsOnline;
            state.IsAlive = member.IsAlive;
            state.LastX = member.X;
            state.LastY = member.Y;
            state.LastGrid = grid;
            state.UpdatedAt = now;
            if (member.IsOnline) state.LastSeenAt = now;

            await NotifyTransitionsAsync(dispatcher, pairing, member, grid, wasOnline, wasAlive, ct);
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task NotifyTransitionsAsync(
        INotificationDispatcher dispatcher, RustPlusPairing pairing, AppTeamInfo.Types.Member member,
        string? grid, bool wasOnline, bool wasAlive, CancellationToken ct)
    {
        string? eventType = null;
        string? body = null;
        var severity = NotificationSeverity.Info;

        if (!wasAlive && member.IsAlive)
        {
            eventType = "TeamMemberRevived";
            body = $"{member.Name} was revived.";
        }
        else if (wasAlive && !member.IsAlive)
        {
            eventType = "TeamMemberDown";
            body = $"{member.Name} died near {grid ?? "an unknown grid"}.";
            severity = NotificationSeverity.Warning;
        }
        else if (!wasOnline && member.IsOnline)
        {
            eventType = "TeamMemberOnline";
            body = $"{member.Name} came online.";
        }
        else if (wasOnline && !member.IsOnline)
        {
            eventType = "TeamMemberOffline";
            body = $"{member.Name} went offline.";
        }

        if (eventType is null) return;

        await dispatcher.DispatchAsync(new DispatchNotificationRequest(
            UserId: pairing.UserId,
            Type: "RustPlusTeamStatus",
            Title: member.Name,
            Body: body!,
            Severity: severity,
            ServerId: pairing.ServerId,
            WebhookEventType: eventType,
            RelatedEntityType: "RustPlusTeamMember",
            RelatedEntityId: null), ct);
    }
}
