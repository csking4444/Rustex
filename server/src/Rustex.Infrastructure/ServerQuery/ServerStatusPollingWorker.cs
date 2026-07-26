using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rustex.Domain;
using Rustex.Domain.Abstractions;
using Rustex.Domain.Entities;
using Rustex.Infrastructure.Persistence;

namespace Rustex.Infrastructure.ServerQuery;

/// <summary>
/// Polls every registered server's query port on an interval via A2S_INFO, persists a
/// ServerStatusSnapshot, updates RustServer.Status, and broadcasts the result. This is real
/// live data (unlike the simulated raid-event pipeline) — any Rust server with a reachable
/// query port works, no plugin or Rust+ pairing required.
/// </summary>
public class ServerStatusPollingWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(20);
    private const int MaxConcurrentQueries = 8;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IServerQueryClient _queryClient;
    private readonly ILogger<ServerStatusPollingWorker> _logger;

    public ServerStatusPollingWorker(
        IServiceScopeFactory scopeFactory,
        IServerQueryClient queryClient,
        ILogger<ServerStatusPollingWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _queryClient = queryClient;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollAllServersAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ServerStatusPollingWorker poll cycle failed");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // shutting down
            }
        }
    }

    private async Task PollAllServersAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var servers = await db.RustServers
            .Where(s => s.QueryPort != null)
            .ToListAsync(ct);

        using var throttle = new SemaphoreSlim(MaxConcurrentQueries);

        var tasks = servers.Select(async server =>
        {
            await throttle.WaitAsync(ct);
            try
            {
                await PollOneServerAsync(server.Id, server.IpAddress, server.QueryPort!.Value, ct);
            }
            finally
            {
                throttle.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    private async Task PollOneServerAsync(Guid serverId, string ipAddress, int queryPort, CancellationToken ct)
    {
        var result = await _queryClient.QueryAsync(ipAddress, queryPort, ct);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var broadcaster = scope.ServiceProvider.GetService<IRaidEventBroadcaster>();

        var server = await db.RustServers.FirstOrDefaultAsync(s => s.Id == serverId, ct);
        if (server is null) return; // deleted between the initial fetch and this poll

        server.Status = result is not null ? ServerStatus.Online : ServerStatus.Offline;
        server.UpdatedAt = DateTimeOffset.UtcNow;

        db.ServerStatusSnapshots.Add(new ServerStatusSnapshot
        {
            ServerId = serverId,
            PingMs = result is not null ? (int)result.RoundTripMs : null,
            PlayerCount = result?.Players,
            MaxPlayers = result?.MaxPlayers,
            QueueSize = null, // A2S_INFO doesn't expose queue size — Rust-specific EDF keyword parsing is a future enhancement
        });

        // Keep the map name in sync with reality when we successfully hear back.
        if (result is not null && !string.IsNullOrWhiteSpace(result.MapName))
            server.MapName = result.MapName;

        await db.SaveChangesAsync(ct);

        if (broadcaster is not null)
        {
            await broadcaster.BroadcastServerStatusUpdatedAsync(serverId, new
            {
                ServerId = serverId,
                Status = server.Status,
                Ping = result?.RoundTripMs,
                Players = result?.Players,
                MaxPlayers = result?.MaxPlayers,
            }, ct);
        }
    }
}
