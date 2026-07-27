using System.Net;
using Microsoft.Extensions.Options;
using Rustex.Infrastructure.Auth;
using Xunit;

namespace Rustex.Api.Tests;

/// <summary>Everything in SteamAuthService is verifiable offline against a stubbed HTTP handler
/// — no live Steam account or network access needed. These tests exist because the previous
/// implementation had none of these checks: no op_endpoint validation, no openid.signed coverage
/// check, no return_to/nonce validation. Each test below corresponds to one of those gaps.</summary>
public class SteamAuthServiceTests
{
    private const string ReturnUrl = "https://api.rustex.test/api/auth/steam/callback";
    private const string Realm = "https://api.rustex.test";
    private const string ValidSteamId = "76561198000000000";

    private static SteamAuthService CreateService(HttpResponseMessage checkAuthResponse)
    {
        var handler = new StubHttpMessageHandler(checkAuthResponse);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://steamcommunity.com") };
        return new SteamAuthService(http, Options.Create(new SteamAuthOptions { ReturnUrl = ReturnUrl, Realm = Realm }));
    }

    private static HttpResponseMessage ValidCheckAuthResponse() =>
        new(HttpStatusCode.OK) { Content = new StringContent("ns:http://specs.openid.net/auth/2.0\nis_valid:true\n") };

    private static HttpResponseMessage InvalidCheckAuthResponse() =>
        new(HttpStatusCode.OK) { Content = new StringContent("ns:http://specs.openid.net/auth/2.0\nis_valid:false\n") };

    /// <summary>Builds a full, otherwise-valid callback query so each test only needs to
    /// override the one field it's exercising.</summary>
    private static Dictionary<string, string> ValidCallback(
        string? returnTo = null,
        string? opEndpoint = "https://steamcommunity.com/openid/login",
        string? signed = "op_endpoint,return_to,response_nonce,assoc_handle,claimed_id,identity",
        string? claimedId = $"https://steamcommunity.com/openid/id/{ValidSteamId}",
        string? identity = $"https://steamcommunity.com/openid/id/{ValidSteamId}",
        string? responseNonce = null)
    {
        var nonce = responseNonce ?? $"{DateTimeOffset.UtcNow:yyyy-MM-ddTHH:mm:ssZ}abc123";
        var dict = new Dictionary<string, string>
        {
            ["openid.mode"] = "id_res",
            ["openid.return_to"] = returnTo ?? $"{ReturnUrl}?state=test-nonce-123",
            ["openid.assoc_handle"] = "handle123",
        };
        if (opEndpoint is not null) dict["openid.op_endpoint"] = opEndpoint;
        if (signed is not null) dict["openid.signed"] = signed;
        if (claimedId is not null) dict["openid.claimed_id"] = claimedId;
        if (identity is not null) dict["openid.identity"] = identity;
        dict["openid.response_nonce"] = nonce;
        return dict;
    }

    [Fact]
    public async Task VerifyAsync_HappyPath_ReturnsSteamIdAndEmbeddedState()
    {
        var service = CreateService(ValidCheckAuthResponse());

        var result = await service.VerifyAsync(ValidCallback(), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(ValidSteamId, result!.SteamId64);
        Assert.Equal("test-nonce-123", result.ReturnToState);
    }

    [Fact]
    public async Task VerifyAsync_ForgedClaimedIdHost_Rejected()
    {
        var service = CreateService(ValidCheckAuthResponse());

        var result = await service.VerifyAsync(
            ValidCallback(claimedId: "https://evil.example.com/openid/id/76561198000000000"), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task VerifyAsync_ClaimedIdIdentityMismatch_Rejected()
    {
        var service = CreateService(ValidCheckAuthResponse());

        var result = await service.VerifyAsync(
            ValidCallback(identity: "https://steamcommunity.com/openid/id/76561198099999999"), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task VerifyAsync_OpEndpointMismatch_Rejected()
    {
        var service = CreateService(ValidCheckAuthResponse());

        var result = await service.VerifyAsync(
            ValidCallback(opEndpoint: "https://evil.example.com/openid/login"), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task VerifyAsync_SignedMissingReturnTo_Rejected()
    {
        var service = CreateService(ValidCheckAuthResponse());

        var result = await service.VerifyAsync(
            ValidCallback(signed: "op_endpoint,response_nonce,assoc_handle,claimed_id,identity"), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task VerifyAsync_SignedMissingResponseNonce_Rejected()
    {
        var service = CreateService(ValidCheckAuthResponse());

        var result = await service.VerifyAsync(
            ValidCallback(signed: "op_endpoint,return_to,assoc_handle,claimed_id,identity"), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task VerifyAsync_ReturnToHostMismatch_Rejected()
    {
        var service = CreateService(ValidCheckAuthResponse());

        var result = await service.VerifyAsync(
            ValidCallback(returnTo: "https://attacker.example.com/api/auth/steam/callback?state=test-nonce-123"), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task VerifyAsync_ReturnToPathMismatch_Rejected()
    {
        var service = CreateService(ValidCheckAuthResponse());

        var result = await service.VerifyAsync(
            ValidCallback(returnTo: "https://api.rustex.test/api/auth/discord/callback?state=test-nonce-123"), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task VerifyAsync_NonceOlderThanFiveMinutes_Rejected()
    {
        var service = CreateService(ValidCheckAuthResponse());
        var staleNonce = $"{DateTimeOffset.UtcNow.AddMinutes(-6):yyyy-MM-ddTHH:mm:ssZ}abc123";

        var result = await service.VerifyAsync(ValidCallback(responseNonce: staleNonce), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task VerifyAsync_NonceInTheFuture_Rejected()
    {
        var service = CreateService(ValidCheckAuthResponse());
        var futureNonce = $"{DateTimeOffset.UtcNow.AddMinutes(6):yyyy-MM-ddTHH:mm:ssZ}abc123";

        var result = await service.VerifyAsync(ValidCallback(responseNonce: futureNonce), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task VerifyAsync_IsValidFalse_Rejected()
    {
        var service = CreateService(InvalidCheckAuthResponse());

        var result = await service.VerifyAsync(ValidCallback(), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task VerifyAsync_ModeNotIdRes_Rejected()
    {
        var service = CreateService(ValidCheckAuthResponse());
        var callback = ValidCallback();
        callback["openid.mode"] = "cancel";

        var result = await service.VerifyAsync(callback, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public void BuildAuthorizeUrl_EmbedsAndEscapesStateInsideReturnTo()
    {
        var service = CreateService(ValidCheckAuthResponse());

        var url = service.BuildAuthorizeUrl(ReturnUrl, Realm, "abc def"); // space forces escaping

        // The return_to param itself is escaped once (it's a query VALUE), and the state inside
        // it is escaped again (return_to is itself a URL with its own query string) — so a
        // literal space becomes %2520, not %20, when embedded two levels deep.
        Assert.Contains(Uri.EscapeDataString($"{ReturnUrl}?state={Uri.EscapeDataString("abc def")}"), url);
    }

    [Fact]
    public void BuildAuthorizeUrl_WithoutForceLogin_TargetsAutoApprovingEndpoint()
    {
        var service = CreateService(ValidCheckAuthResponse());

        var url = service.BuildAuthorizeUrl(ReturnUrl, Realm, "nonce");

        Assert.StartsWith("https://steamcommunity.com/openid/login?", url);
        Assert.DoesNotContain("loginform", url);
    }

    [Fact]
    public void BuildAuthorizeUrl_WithForceLogin_TargetsSignInFormAndCarriesRequestInGoto()
    {
        var service = CreateService(ValidCheckAuthResponse());

        var url = service.BuildAuthorizeUrl(ReturnUrl, Realm, "nonce", forceLogin: true);

        Assert.StartsWith("https://steamcommunity.com/openid/loginform/?goto=", url);

        // goto is a steamcommunity-relative path, so the whole OpenID request has to survive one
        // extra layer of escaping and still resume correctly after the user signs in.
        var goto_ = Uri.UnescapeDataString(url["https://steamcommunity.com/openid/loginform/?goto=".Length..]);
        Assert.StartsWith("/openid/login?", goto_);
        Assert.Contains("openid.mode=checkid_setup", goto_);
        Assert.Contains(Uri.EscapeDataString($"{ReturnUrl}?state=nonce"), goto_);
    }

    private sealed class StubHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(response);
    }
}
