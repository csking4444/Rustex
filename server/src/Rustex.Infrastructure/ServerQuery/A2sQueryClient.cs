using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Rustex.Infrastructure.ServerQuery;

/// <summary>
/// Minimal Source engine A2S_INFO client (UDP). Handles the challenge/response handshake
/// newer servers require. Deliberately does not handle multi-packet (split) responses —
/// A2S_INFO replies are small enough that Rust servers respond in a single UDP packet in
/// practice; a split response is treated as a failed query rather than reassembled.
/// </summary>
public class A2sQueryClient : IServerQueryClient
{
    private static readonly byte[] InfoQueryPrefix =
    {
        0xFF, 0xFF, 0xFF, 0xFF, 0x54,
    };
    private static readonly byte[] InfoQuerySuffix = Encoding.ASCII.GetBytes("Source Engine Query\0");

    private readonly ILogger<A2sQueryClient> _logger;
    private readonly TimeSpan _timeout = TimeSpan.FromSeconds(3);

    public A2sQueryClient(ILogger<A2sQueryClient> logger) => _logger = logger;

    public async Task<A2sInfoResult?> QueryAsync(string host, int queryPort, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_timeout);

        try
        {
            using var udp = new UdpClient();
            udp.Connect(host, queryPort);

            var request = BuildRequest();
            await udp.SendAsync(request, cts.Token);

            var response = await udp.ReceiveAsync(cts.Token);
            var payload = response.Buffer;

            // Challenge response ('A') — resend the request with the challenge bytes appended.
            if (payload.Length >= 5 && payload[4] == 0x41)
            {
                var challenge = payload[5..9];
                var challengedRequest = new byte[request.Length + 4];
                Buffer.BlockCopy(request, 0, challengedRequest, 0, request.Length);
                Buffer.BlockCopy(challenge, 0, challengedRequest, request.Length, 4);

                await udp.SendAsync(challengedRequest, cts.Token);
                response = await udp.ReceiveAsync(cts.Token);
                payload = response.Buffer;
            }

            stopwatch.Stop();
            return ParseInfoResponse(payload, stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("A2S_INFO query to {Host}:{Port} timed out", host, queryPort);
            return null;
        }
        catch (SocketException ex)
        {
            _logger.LogDebug(ex, "A2S_INFO query to {Host}:{Port} failed", host, queryPort);
            return null;
        }
    }

    private static byte[] BuildRequest()
    {
        var request = new byte[InfoQueryPrefix.Length + InfoQuerySuffix.Length];
        Buffer.BlockCopy(InfoQueryPrefix, 0, request, 0, InfoQueryPrefix.Length);
        Buffer.BlockCopy(InfoQuerySuffix, 0, request, InfoQueryPrefix.Length, InfoQuerySuffix.Length);
        return request;
    }

    private static A2sInfoResult? ParseInfoResponse(byte[] payload, long roundTripMs)
    {
        // Header: FF FF FF FF 'I' (0x49)
        if (payload.Length < 6 || payload[4] != 0x49) return null;

        var offset = 5;
        offset += 1; // protocol version, unused

        var name = ReadCString(payload, ref offset);
        var map = ReadCString(payload, ref offset);
        ReadCString(payload, ref offset); // folder, unused
        ReadCString(payload, ref offset); // game, unused

        offset += 2; // steam app id (short)

        var players = payload[offset++];
        var maxPlayers = payload[offset++];
        var bots = payload[offset++];

        offset += 1; // server type
        offset += 1; // environment
        offset += 1; // visibility
        offset += 1; // VAC

        var version = offset < payload.Length ? ReadCString(payload, ref offset) : "unknown";

        return new A2sInfoResult(name, map, players, maxPlayers, bots, version, roundTripMs);
    }

    private static string ReadCString(byte[] buffer, ref int offset)
    {
        var start = offset;
        while (offset < buffer.Length && buffer[offset] != 0x00) offset++;

        var value = Encoding.UTF8.GetString(buffer, start, offset - start);
        if (offset < buffer.Length) offset++; // skip the null terminator
        return value;
    }
}
