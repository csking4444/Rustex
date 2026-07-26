using Microsoft.Extensions.Logging;

namespace Rustex.Infrastructure.RustPlus.Fcm;

/// <summary>
/// Registers a checked-in Android device for GCM/FCM push notifications against the Rust+
/// companion app's Firebase project, so Facepunch's servers have somewhere to push pairing
/// events. See docs/RUSTPLUS.md — best-effort, unverified against a live call. Requires
/// RustPlus:FcmSenderId (the Rust+ app's Firebase sender/project ID) to be configured; this
/// project does not guess or hardcode that value.
/// </summary>
public sealed class FcmRegistrationClient
{
    private const string RegisterUrl = "https://android.clients.google.com/c2dm/register3";
    private const string RustCompanionAppId = "com.facepunch.rust.companion";

    private readonly HttpClient _http;
    private readonly ILogger<FcmRegistrationClient> _logger;

    public FcmRegistrationClient(HttpClient http, ILogger<FcmRegistrationClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<string> RegisterAsync(ulong androidId, ulong securityToken, string senderId, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, RegisterUrl);
        request.Headers.Add("Authorization", $"AidLogin {androidId}:{securityToken}");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["app"] = RustCompanionAppId,
            ["X-subtype"] = senderId,
            ["sender"] = senderId,
            ["device"] = androidId.ToString(),
            ["app_ver"] = "1",
            ["gcm_ver"] = "170817",
        });

        using var response = await _http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        var tokenLine = body.Split('\n').FirstOrDefault(l => l.StartsWith("token=", StringComparison.Ordinal));
        if (tokenLine is null)
        {
            _logger.LogWarning("FCM registration did not return a token (response: {Body}). See docs/RUSTPLUS.md.", body);
            throw new InvalidOperationException($"FCM registration failed: {body}");
        }

        return tokenLine["token=".Length..];
    }
}
