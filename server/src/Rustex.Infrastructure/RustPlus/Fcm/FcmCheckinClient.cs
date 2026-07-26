using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Rustex.Infrastructure.RustPlus.Fcm.Proto;

namespace Rustex.Infrastructure.RustPlus.Fcm;

public sealed record CheckinResult(ulong AndroidId, ulong SecurityToken);

/// <summary>
/// Registers a fake Android device with Google's check-in service to get an androidId +
/// securityToken, which the GCM/FCM registration call needs. See docs/RUSTPLUS.md — best-effort,
/// unverified against a live call.
/// </summary>
public sealed class FcmCheckinClient
{
    private const string CheckinUrl = "https://android.clients.google.com/checkin";
    private readonly HttpClient _http;
    private readonly ILogger<FcmCheckinClient> _logger;

    public FcmCheckinClient(HttpClient http, ILogger<FcmCheckinClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<CheckinResult> CheckinAsync(CancellationToken ct)
    {
        var request = new AndroidCheckinRequest
        {
            Id = "0",
            Checkin = new AndroidCheckinProto { Version = "1" },
            Version = 3,
            UserSerialNumber = "0",
        };

        using var content = new ByteArrayContent(request.ToByteArray());
        content.Headers.Add("Content-Type", "application/x-protobuf");

        using var response = await _http.PostAsync(CheckinUrl, content, ct);
        response.EnsureSuccessStatusCode();

        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        var parsed = AndroidCheckinResponse.Parser.ParseFrom(bytes);

        if (parsed.AndroidId == 0 || parsed.SecurityToken == 0)
        {
            _logger.LogWarning("FCM check-in returned no androidId/securityToken — the checkin.proto schema in this project likely doesn't match Google's real one. See docs/RUSTPLUS.md.");
            throw new InvalidOperationException("FCM check-in did not return usable credentials.");
        }

        return new CheckinResult(parsed.AndroidId, parsed.SecurityToken);
    }
}
