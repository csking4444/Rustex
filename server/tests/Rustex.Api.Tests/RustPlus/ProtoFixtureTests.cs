using Google.Protobuf;
using Rustex.Infrastructure.RustPlus.Proto;
using Xunit;

namespace Rustex.Api.Tests.RustPlus;

/// <summary>
/// The previous rustplus.proto had ~20 wrong field numbers — every AppResponse field was off,
/// AppBroadcast was 1/2/3 instead of 4/5/6, sellOrders was field 12 instead of 13, playerToken
/// was uint32 instead of int32 — and nothing caught any of it, because nothing had ever actually
/// round-tripped a message through it. These fixtures close that gap.
///
/// Each .bin fixture below was generated ONCE with `protoc --encode` against the upstream
/// community schema (https://github.com/liamcottle/rustplus.js/blob/master/rustplus.proto),
/// completely independent of Rustex.Infrastructure/RustPlus/Proto/rustplus.proto — so a bug in
/// our .proto can't also be baked into the fixture that's supposed to catch it. Regenerate with:
///
///   protoc --encode=rustplus.AppMessage upstream_rustplus.proto &lt; teaminfo_response.txtpb &gt; teaminfo_response.bin
///   (same pattern for the other AppMessage fixtures; --encode=rustplus.AppRequest for the req_* ones)
///
/// then base64 the .bin file into the corresponding constant below.
/// </summary>
public class ProtoFixtureTests
{
    // ---- AppMessage fixtures (parse direction) ----

    private const string TeamInfoResponseB64 =
        "Cl8IKkpbCIGY+ZKQgICIARIoCIGY+ZKQgICIARIJS2FlbHRob3JuHQAQlkQlABBIRCgBMOgHOAFAABIlCIKY+ZKQgICIARIFVmV4ZW4dAECXRCUAwEZEKAAw0A84AECIJw==";

    private const string VendingResponseB64 =
        "ClwIB2pYClYI9QMQAx0AQE5FJQBAA0VaCkJvYidzIFNob3BgAGoZCM4fEAEYt/a+w/z/////ASC0ASgFMAA4AGocCKy6rJcBEMgBGLf2vsP8/////wEgBSgoMAA4AA==";

    private const string TeamMessageBroadcastB64 =
        "EiwqKgooCIGY+ZKQgICIARIJS2FlbHRob3JuGgQhcG9wIgcjOGJmZjZiKMDEBw==";

    private const string EntityChangedBroadcastB64 =
        "EhAyDgi5hQYSCAgBGAAgACgA";

    // ---- AppRequest fixtures (serialize direction) ----

    private const string ReqGetMapMarkersB64 = "CAEQgZj5kpCAgIgBGM7+3/7//////wGSAQA=";
    private const string ReqSetSubscriptionB64 = "CAIQgZj5kpCAgIgBGM7+3/7//////wGKAQIIAQ==";
    private const string ReqNegativeTokenB64 = "CAMQgZj5kpCAgIgBGMrzlf///////wFqBwoFaGVsbG8=";

    private static byte[] B64(string s) => Convert.FromBase64String(s);

    [Fact]
    public void ParsesTeamInfoResponse_WithCorrectFieldNumbers()
    {
        var message = AppMessage.Parser.ParseFrom(B64(TeamInfoResponseB64));

        var response = message.Response;
        Assert.NotNull(response);
        Assert.Equal(42u, response.Seq);

        var teamInfo = response.TeamInfo;
        Assert.NotNull(teamInfo); // fails under the old schema, where response.teamInfo was field 7 not 9
        Assert.Equal(76561198000000001UL, teamInfo.LeaderSteamId);
        Assert.Equal(2, teamInfo.Members.Count);

        var leader = teamInfo.Members[0];
        Assert.Equal("Kaelthorn", leader.Name);
        Assert.True(leader.IsOnline);
        Assert.True(leader.IsAlive);

        var dead = teamInfo.Members[1];
        Assert.Equal("Vexen", dead.Name);
        Assert.False(dead.IsOnline);
        Assert.False(dead.IsAlive);
        Assert.Equal(5000u, dead.DeathTime);
    }

    [Fact]
    public void ParsesVendingResponse_WithCorrectSellOrdersFieldNumber()
    {
        var message = AppMessage.Parser.ParseFrom(B64(VendingResponseB64));

        var markers = message.Response?.MapMarkers?.Markers;
        Assert.NotNull(markers);
        var marker = Assert.Single(markers);

        Assert.Equal(501u, marker.Id);
        Assert.Equal(AppMarkerType.VendingMachine, marker.Type);
        Assert.Equal("Bob's Shop", marker.Name); // fails under the old schema, which had no `name` field at all
        Assert.False(marker.OutOfStock);

        // This is the one that mattered most: sellOrders was field 12 in the old schema (actually
        // outOfStock upstream) and field 13 was never read at all — /vending-machines silently
        // returned every marker with an empty sell-order list.
        Assert.Equal(2, marker.SellOrders.Count);
        Assert.Equal(4046, marker.SellOrders[0].ItemId);
        Assert.Equal(180, marker.SellOrders[0].CostPerItem);
        Assert.Equal(5, marker.SellOrders[0].AmountInStock);
    }

    [Fact]
    public void ParsesTeamMessageBroadcast_ThroughTheNewTeamMessageWrapper()
    {
        var message = AppMessage.Parser.ParseFrom(B64(TeamMessageBroadcastB64));

        var broadcast = message.Broadcast;
        Assert.NotNull(broadcast); // fails under the old schema, where broadcast fields were 1/2/3 not 4/5/6
        Assert.NotNull(broadcast.TeamMessage);

        var teamMessage = broadcast.TeamMessage.Message;
        Assert.Equal("Kaelthorn", teamMessage.Name);
        Assert.Equal("!pop", teamMessage.Message);
        Assert.Equal("#8bff6b", teamMessage.Color); // fails if Color were still typed as int32
    }

    [Fact]
    public void ParsesEntityChangedBroadcast()
    {
        var message = AppMessage.Parser.ParseFrom(B64(EntityChangedBroadcastB64));

        var entityChanged = message.Broadcast?.EntityChanged;
        Assert.NotNull(entityChanged);
        Assert.Equal(99001u, entityChanged.EntityId);
        Assert.True(entityChanged.Payload.Value);
    }

    [Fact]
    public void SerializesGetMapMarkersRequest_OnField18NotSetSubscriptionsField17()
    {
        // Under the old schema, getMapMarkers was field 17 — which upstream is setSubscription.
        // GetMapMarkersAsync had therefore been sending a malformed "set my subscription" call
        // and never actually requesting map markers at all.
        var request = new AppRequest
        {
            Seq = 1,
            PlayerId = 76561198000000001,
            PlayerToken = -2621618,
            GetMapMarkers = new AppEmpty(),
        };

        Assert.Equal(B64(ReqGetMapMarkersB64), request.ToByteArray());
    }

    [Fact]
    public void SerializesSetSubscriptionRequest_OnField17()
    {
        var request = new AppRequest
        {
            Seq = 2,
            PlayerId = 76561198000000001,
            PlayerToken = -2621618,
            SetSubscription = new AppFlag { Value = true },
        };

        Assert.Equal(B64(ReqSetSubscriptionB64), request.ToByteArray());
    }

    [Fact]
    public void SerializesAndRoundTrips_NegativePlayerToken()
    {
        // The old schema declared playerToken as uint32; real Rust+ tokens are signed and
        // negative roughly half the time, which made a negative token simply impossible to send.
        var request = new AppRequest
        {
            Seq = 3,
            PlayerId = 76561198000000001,
            PlayerToken = -1738294,
            SendTeamMessage = new AppSendMessage { Message = "hello" },
        };

        var bytes = request.ToByteArray();
        Assert.Equal(B64(ReqNegativeTokenB64), bytes);

        var roundTripped = AppRequest.Parser.ParseFrom(bytes);
        Assert.Equal(-1738294, roundTripped.PlayerToken);
    }
}
