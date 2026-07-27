using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rustex.Domain;
using Rustex.Domain.Entities;
using Rustex.Domain.RustPlus;
using Rustex.Infrastructure.Persistence;
using Rustex.Infrastructure.RustPlus.Proto;

namespace Rustex.Infrastructure.RustPlus;

/// <summary>
/// Ingests team chat (teamMessage broadcast) into RustPlusChatMessage and answers the small
/// !command set from TeamChatCommandParser. The parser already refuses to parse the bot's own
/// messages (senderSteamId == pairing.PlayerId) — the loop guard that matters most — on top of
/// that this rate-limits replies per pairing so a burst of chatter can't spam the team.
/// </summary>
public sealed class RustPlusChatAssistantWorker : BackgroundService
{
    private const int MaxRepliesPerMinute = 20;
    private static readonly TimeSpan MinReplyGap = TimeSpan.FromSeconds(3);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RustPlusConnectionManager _connectionManager;
    private readonly ILogger<RustPlusChatAssistantWorker> _logger;
    private readonly ConcurrentDictionary<Guid, ReplyThrottle> _throttles = new();
    private readonly Channel<(Guid PairingId, AppTeamMessage Message)> _channel =
        Channel.CreateBounded<(Guid, AppTeamMessage)>(new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.DropOldest });

    public RustPlusChatAssistantWorker(IServiceScopeFactory scopeFactory, RustPlusConnectionManager connectionManager, ILogger<RustPlusChatAssistantWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _connectionManager = connectionManager;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _connectionManager.OnBroadcast += OnBroadcast;
        stoppingToken.Register(() => _connectionManager.OnBroadcast -= OnBroadcast);
        return ConsumeAsync(stoppingToken);
    }

    private void OnBroadcast(Guid pairingId, AppBroadcast broadcast)
    {
        if (broadcast.TeamMessage?.Message is { } message)
            _channel.Writer.TryWrite((pairingId, message));
    }

    private async Task ConsumeAsync(CancellationToken ct)
    {
        await foreach (var (pairingId, message) in _channel.Reader.ReadAllAsync(ct))
        {
            try
            {
                await ProcessAsync(pairingId, message, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process team chat message for pairing {PairingId}", pairingId);
            }
        }
    }

    private async Task ProcessAsync(Guid pairingId, AppTeamMessage message, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var pairing = await db.RustPlusPairings.Include(p => p.Server).FirstOrDefaultAsync(p => p.Id == pairingId, ct);
        if (pairing is null) return;

        db.RustPlusChatMessages.Add(new RustPlusChatMessage
        {
            ServerId = pairing.ServerId,
            SteamId = message.SteamId,
            Name = message.Name ?? "Unknown",
            Message = message.Message ?? "",
            IsFromAssistant = false,
        });
        await db.SaveChangesAsync(ct);

        var parsed = TeamChatCommandParser.TryParse(message.Message ?? "", message.SteamId, pairing.PlayerId);
        if (parsed is null) return;

        if (!_throttles.GetOrAdd(pairingId, _ => new ReplyThrottle()).TryConsume())
        {
            _logger.LogDebug("Rate-limited a chat assistant reply for pairing {PairingId}", pairingId);
            return;
        }

        if (!_connectionManager.TryGetClient(pairingId, out var client) || client is null) return;

        var reply = await BuildReplyAsync(db, client, pairing, message, parsed, ct);
        if (reply is null) return;

        try
        {
            await client.SendTeamMessageAsync(reply, ct);
            db.RustPlusChatMessages.Add(new RustPlusChatMessage
            {
                ServerId = pairing.ServerId,
                SteamId = pairing.PlayerId,
                Name = "Rustex",
                Message = reply,
                IsFromAssistant = true,
            });
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send chat assistant reply for pairing {PairingId}", pairingId);
        }
    }

    private static async Task<string?> BuildReplyAsync(
        AppDbContext db, RustPlusClient client, RustPlusPairing pairing, AppTeamMessage message, ParsedTeamChatCommand parsed, CancellationToken ct)
    {
        switch (parsed.Command)
        {
            case TeamChatCommand.Help:
                return "Commands: !pop !time !team !alerts !wipe !pos !device <name>";

            case TeamChatCommand.Pop:
                try
                {
                    var info = await client.GetInfoAsync(ct);
                    return $"{info.Players}/{info.MaxPlayers} players online ({info.QueuedPlayers} queued)";
                }
                catch { return "Couldn't reach the server for population right now."; }

            case TeamChatCommand.Time:
                try
                {
                    var time = await client.GetTimeAsync(ct);
                    var hours = (int)time.Time;
                    var minutes = (int)((time.Time - hours) * 60);
                    return $"In-game time: {hours:D2}:{minutes:D2}";
                }
                catch { return "Couldn't reach the server for the time right now."; }

            case TeamChatCommand.Team:
                {
                    var members = await db.RustPlusTeamMemberStates.Where(s => s.ServerId == pairing.ServerId).ToListAsync(ct);
                    var online = members.Count(m => m.IsOnline);
                    return $"{online}/{members.Count} team members online";
                }

            case TeamChatCommand.Alerts:
                {
                    var alertCount = await db.ShopAlerts.CountAsync(a => a.ServerId == pairing.ServerId && a.IsEnabled, ct);
                    var activeRaids = await db.RaidEvents.CountAsync(r => r.ServerId == pairing.ServerId && r.Status == RaidStatus.Active, ct);
                    return $"{alertCount} shop alert(s) enabled, {activeRaids} active raid alert(s)";
                }

            case TeamChatCommand.Wipe:
                try
                {
                    var info = await client.GetInfoAsync(ct);
                    var wipedAt = DateTimeOffset.FromUnixTimeSeconds(info.WipeTime);
                    return $"Last wipe: {wipedAt:yyyy-MM-dd} ({(DateTimeOffset.UtcNow - wipedAt).Days}d ago)";
                }
                catch { return "Couldn't reach the server for wipe info right now."; }

            case TeamChatCommand.Pos:
                {
                    var self = await db.RustPlusTeamMemberStates.FirstOrDefaultAsync(s => s.ServerId == pairing.ServerId && s.SteamId == message.SteamId, ct);
                    return self?.LastGrid is { } grid ? $"{message.Name}'s last known position: {grid}" : $"No known position for {message.Name} yet.";
                }

            case TeamChatCommand.Device:
                {
                    var devices = await db.RustPlusSmartDevices.Where(d => d.ServerId == pairing.ServerId).ToListAsync(ct);
                    if (!string.IsNullOrWhiteSpace(parsed.Argument))
                    {
                        var match = devices.FirstOrDefault(d => d.Name.Contains(parsed.Argument, StringComparison.OrdinalIgnoreCase));
                        if (match is null) return $"No device matching \"{parsed.Argument}\".";
                        return match.LastKnownValue is { } v ? $"{match.Name}: {(v ? "on/triggered" : "off")}" : $"{match.Name}: unknown state";
                    }
                    return devices.Count == 0 ? "No smart devices paired yet." : $"Paired devices: {string.Join(", ", devices.Select(d => d.Name))}";
                }

            default:
                return "Unknown command. Try !help.";
        }
    }

    /// <summary>Per-pairing sliding-window limiter: at most MaxRepliesPerMinute replies, no two
    /// closer together than MinReplyGap — cheap enough to keep in memory since a lost counter on
    /// restart just means a brief extra allowance, not a correctness bug.</summary>
    private sealed class ReplyThrottle
    {
        private readonly object _lock = new();
        private readonly Queue<DateTimeOffset> _sentAt = new();
        private DateTimeOffset _lastSentAt = DateTimeOffset.MinValue;

        public bool TryConsume()
        {
            lock (_lock)
            {
                var now = DateTimeOffset.UtcNow;
                if (now - _lastSentAt < MinReplyGap) return false;

                while (_sentAt.Count > 0 && now - _sentAt.Peek() > TimeSpan.FromMinutes(1))
                    _sentAt.Dequeue();
                if (_sentAt.Count >= MaxRepliesPerMinute) return false;

                _sentAt.Enqueue(now);
                _lastSentAt = now;
                return true;
            }
        }
    }
}
