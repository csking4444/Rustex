using System.Text.Json;
using Microsoft.Extensions.Logging;
using Rustex.Infrastructure.RustPlus.Fcm.Proto;

namespace Rustex.Infrastructure.RustPlus.Fcm;

public sealed record RustPlusAutoPairingResult(string ServerIp, int ServerPort, ulong PlayerId, string PlayerToken);

/// <summary>
/// Orchestrates one end-to-end auto-pairing attempt: FCM check-in → FCM registration → tell
/// Facepunch which push token belongs to this Steam account → open an MCS connection and wait
/// for the player to run "app.pair" in-game, which Facepunch pushes as a pairing payload.
///
/// This is the experimental, best-effort part of Rust+ integration — see docs/RUSTPLUS.md before
/// debugging this. Each stage can fail independently; failures here most likely mean one of the
/// unverified schema/URL assumptions documented there needs correcting against a known-good
/// reference implementation, not that the overall approach is wrong.
/// </summary>
public sealed class RustPlusAutoPairingSession
{
    private readonly FcmCheckinClient _checkin;
    private readonly FcmRegistrationClient _registration;
    private readonly FacepunchPushRegistrationClient _facepunchRegistration;
    private readonly McsClient _mcs;
    private readonly ILogger<RustPlusAutoPairingSession> _logger;
    private readonly string _fcmSenderId;

    public RustPlusAutoPairingSession(
        FcmCheckinClient checkin,
        FcmRegistrationClient registration,
        FacepunchPushRegistrationClient facepunchRegistration,
        McsClient mcs,
        string fcmSenderId,
        ILogger<RustPlusAutoPairingSession> logger)
    {
        _checkin = checkin;
        _registration = registration;
        _facepunchRegistration = facepunchRegistration;
        _mcs = mcs;
        _fcmSenderId = fcmSenderId;
        _logger = logger;
    }

    public async Task<RustPlusAutoPairingResult> WaitForPairingAsync(string steamAuthTicketHex, TimeSpan timeout, CancellationToken ct)
    {
        var checkinResult = await _checkin.CheckinAsync(ct);
        var fcmToken = await _registration.RegisterAsync(checkinResult.AndroidId, checkinResult.SecurityToken, _fcmSenderId, ct);
        await _facepunchRegistration.RegisterAsync(steamAuthTicketHex, fcmToken, ct);

        var tcs = new TaskCompletionSource<RustPlusAutoPairingResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _mcs.OnDataMessage += stanza => TryHandle(stanza, tcs);

        await _mcs.ConnectAndLoginAsync(checkinResult.AndroidId, checkinResult.SecurityToken, fcmToken, ct);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        await using (timeoutCts.Token.Register(() => tcs.TrySetCanceled(timeoutCts.Token)))
        {
            return await tcs.Task;
        }
    }

    private void TryHandle(DataMessageStanza stanza, TaskCompletionSource<RustPlusAutoPairingResult> tcs)
    {
        try
        {
            var bodyEntry = stanza.AppData.FirstOrDefault(kv => kv.Key == "body");
            if (bodyEntry is null)
            {
                _logger.LogDebug("MCS push had no 'body' app_data entry — keys were: {Keys}", string.Join(",", stanza.AppData.Select(kv => kv.Key)));
                return;
            }

            using var doc = JsonDocument.Parse(bodyEntry.Value);
            var root = doc.RootElement;

            if (!TryGetCaseInsensitive(root, "ip", out var ip) ||
                !TryGetCaseInsensitive(root, "port", out var portStr) ||
                !TryGetCaseInsensitive(root, "playerId", out var playerIdStr) ||
                !TryGetCaseInsensitive(root, "playerToken", out var playerTokenStr))
            {
                _logger.LogDebug("MCS pairing payload was missing an expected field: {Body}", bodyEntry.Value);
                return;
            }

            var result = new RustPlusAutoPairingResult(ip, int.Parse(portStr), ulong.Parse(playerIdStr), playerTokenStr);
            tcs.TrySetResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse an MCS data message as a Rust+ pairing payload");
        }
    }

    private static bool TryGetCaseInsensitive(JsonElement root, string name, out string value)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString()! : property.Value.GetRawText();
                return true;
            }
        }
        value = "";
        return false;
    }
}
