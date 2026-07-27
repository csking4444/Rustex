using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rustex.Infrastructure.Persistence;
using Rustex.Infrastructure.Security;

namespace Rustex.Infrastructure.RustPlus;

/// <summary>
/// Without this, a RustPlusSession only exists after some HTTP request calls
/// RustPlusConnectionManager.GetOrConnectAsync — fine for on-demand endpoints like /team, but it
/// means anything that consumes broadcasts in the background (the vending poll worker, the chat
/// assistant) would silently do nothing after every API restart until a user happened to hit an
/// endpoint for that pairing. This opens every saved pairing's session eagerly instead.
///
/// Runs a reconcile pass on startup and periodically after — periodic, not just once, so a
/// pairing created while this instance is already running still gets warmed for background
/// consumers rather than waiting on the next restart.
/// </summary>
public class RustPlusSessionWarmupWorker : BackgroundService
{
    private static readonly TimeSpan ReconcileInterval = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RustPlusConnectionManager _connectionManager;
    private readonly ILogger<RustPlusSessionWarmupWorker> _logger;

    public RustPlusSessionWarmupWorker(
        IServiceScopeFactory scopeFactory,
        RustPlusConnectionManager connectionManager,
        ILogger<RustPlusSessionWarmupWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _connectionManager = connectionManager;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await WarmAllPairingsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RustPlusSessionWarmupWorker reconcile pass failed");
            }

            try
            {
                await Task.Delay(ReconcileInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // shutting down
            }
        }
    }

    private async Task WarmAllPairingsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var encryption = scope.ServiceProvider.GetService<IEncryptionService>();

        if (encryption is null)
        {
            _logger.LogDebug("No Encryption:FieldKey configured — skipping Rust+ session warmup.");
            return;
        }

        var pairings = await db.RustPlusPairings.ToListAsync(ct);

        foreach (var pairing in pairings)
        {
            if (!int.TryParse(encryption.Decrypt(pairing.PlayerTokenEncrypted), out var token))
            {
                _logger.LogWarning("Skipping Rust+ warmup for pairing {PairingId} — stored token isn't a valid 32-bit value", pairing.Id);
                continue;
            }

            _connectionManager.EnsureSession(pairing, token);
        }
    }
}
