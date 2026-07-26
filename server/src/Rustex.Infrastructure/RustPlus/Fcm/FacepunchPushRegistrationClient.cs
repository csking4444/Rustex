using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace Rustex.Infrastructure.RustPlus.Fcm;

/// <summary>
/// Tells Facepunch's companion backend which FCM push token belongs to which Steam account, so
/// that when a player runs "app.pair" in-game, Facepunch's servers know where to push the
/// pairing payload. See docs/RUSTPLUS.md — this call requires a Steam auth session ticket for
/// the Rust app (AppID 252490), which can only be minted by a live Steam client session. This
/// project cannot generate that ticket itself (doing so would require holding a live Steam game
/// session, which is out of scope for a server-side app — see the "no Steam credentials" note
/// in docs/RUSTPLUS.md). The ticket must be supplied by the user, obtained through their own
/// Steam client.
/// </summary>
public sealed class FacepunchPushRegistrationClient
{
    private const string RegisterUrl = "https://companion-rust.facepunch.com/api/push/register";

    private readonly HttpClient _http;
    private readonly ILogger<FacepunchPushRegistrationClient> _logger;

    public FacepunchPushRegistrationClient(HttpClient http, ILogger<FacepunchPushRegistrationClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task RegisterAsync(string steamAuthTicketHex, string fcmToken, CancellationToken ct)
    {
        var body = new
        {
            AuthToken = steamAuthTicketHex,
            DeviceId = $"rustex-{Guid.NewGuid():N}",
            PushKind = 0, // best-effort guess: 0 = FCM/Android in Facepunch's enum — unverified
            PushToken = fcmToken,
        };

        using var response = await _http.PostAsJsonAsync(RegisterUrl, body, ct);
        if (!response.IsSuccessStatusCode)
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("Facepunch push registration failed ({Status}): {Body}. See docs/RUSTPLUS.md.", response.StatusCode, text);
            throw new InvalidOperationException($"Facepunch push registration failed: {response.StatusCode}");
        }
    }
}
