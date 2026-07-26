using System.Net.Security;
using System.Net.Sockets;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Rustex.Infrastructure.RustPlus.Fcm.Proto;

namespace Rustex.Infrastructure.RustPlus.Fcm;

/// <summary>
/// A persistent connection to Google's MCS (Mobile Connection Server), the thing that actually
/// delivers FCM push payloads to a "device". This is the least-verified piece of the whole
/// project — see docs/RUSTPLUS.md. The wire *framing* here (version byte once, then repeated
/// [tag byte][varint length][protobuf payload] frames) is a stable, mechanical structure that's
/// well-documented across community implementations. The *tag-to-message-type mapping* and the
/// field numbers inside each message (mcs.proto) are best-effort and may not be exactly right.
/// </summary>
public sealed class McsClient : IAsyncDisposable
{
    private const string Host = "mtalk.google.com";
    private const int Port = 5228;
    private const byte McsVersion = 41;

    // Best-effort tag mapping — see class remarks. If pairing pushes never arrive, this mapping
    // (and the mcs.proto field numbers) is the first thing to re-derive from a known-good
    // reference implementation.
    private const byte TagHeartbeatPing = 0;
    private const byte TagHeartbeatAck = 1;
    private const byte TagLoginRequest = 2;
    private const byte TagLoginResponse = 3;
    private const byte TagClose = 4;
    private const byte TagIqStanza = 7;
    private const byte TagDataMessageStanza = 8;

    private readonly ILogger<McsClient> _logger;
    private TcpClient? _tcp;
    private SslStream? _ssl;
    private Task? _readLoop;
    private readonly CancellationTokenSource _lifetimeCts = new();

    public event Action<DataMessageStanza>? OnDataMessage;
    public event Action? OnLoginComplete;

    public McsClient(ILogger<McsClient> logger) => _logger = logger;

    public async Task ConnectAndLoginAsync(ulong androidId, ulong securityToken, string fcmToken, CancellationToken ct)
    {
        _tcp = new TcpClient();
        await _tcp.ConnectAsync(Host, Port, ct);

        _ssl = new SslStream(_tcp.GetStream(), leaveInnerStreamOpen: false);
        await _ssl.AuthenticateAsClientAsync(Host);

        var login = new LoginRequest
        {
            Id = "chrome-63.0.3234.0",
            Domain = "mcs.android.com",
            User = androidId.ToString(),
            Resource = androidId.ToString(),
            AuthToken = securityToken.ToString(),
            DeviceId = $"android-{androidId:x}",
            NetworkType = 1,
            UseRmq2 = true,
            LastRmqId = 1,
        };

        var payload = login.ToByteArray();
        await _ssl.WriteAsync(new[] { McsVersion, TagLoginRequest }, ct);
        await WriteVarintAndPayloadAsync(_ssl, payload, ct);
        await _ssl.FlushAsync(ct);

        _readLoop = Task.Run(() => ReadLoopAsync(_lifetimeCts.Token), CancellationToken.None);
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        if (_ssl is null) return;

        try
        {
            // First frame after connecting includes the version byte; every subsequent frame
            // is just [tag][varint length][payload].
            var version = await ReadByteAsync(_ssl, ct);
            if (version != McsVersion)
                _logger.LogWarning("MCS server reported version {Version}, expected {Expected}", version, McsVersion);

            while (!ct.IsCancellationRequested)
            {
                var tag = await ReadByteAsync(_ssl, ct);
                var length = await ReadVarintAsync(_ssl, ct);
                var payload = new byte[length];
                await ReadExactAsync(_ssl, payload, ct);

                switch (tag)
                {
                    case TagLoginResponse:
                        var loginResponse = LoginResponse.Parser.ParseFrom(payload);
                        if (loginResponse.Error is not null)
                            _logger.LogWarning("MCS login returned an error: {Error}", loginResponse.Error.Message);
                        else
                            OnLoginComplete?.Invoke();
                        break;

                    case TagDataMessageStanza:
                        OnDataMessage?.Invoke(DataMessageStanza.Parser.ParseFrom(payload));
                        break;

                    case TagHeartbeatPing:
                        await SendFrameAsync(TagHeartbeatAck, new HeartbeatAck().ToByteArray(), ct);
                        break;

                    case TagClose:
                        _logger.LogInformation("MCS server closed the connection");
                        return;

                    case TagIqStanza:
                        break; // keepalive/ack plumbing we don't need to act on

                    default:
                        _logger.LogDebug("Unhandled MCS tag {Tag} ({Length} bytes) — see docs/RUSTPLUS.md for the tag-mapping caveat", tag, length);
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MCS read loop ended unexpectedly");
        }
    }

    private async Task SendFrameAsync(byte tag, byte[] payload, CancellationToken ct)
    {
        if (_ssl is null) return;
        await _ssl.WriteAsync(new[] { tag }, ct);
        await WriteVarintAndPayloadAsync(_ssl, payload, ct);
        await _ssl.FlushAsync(ct);
    }

    private static async Task WriteVarintAndPayloadAsync(Stream stream, byte[] payload, CancellationToken ct)
    {
        var lengthBytes = EncodeVarint((uint)payload.Length);
        await stream.WriteAsync(lengthBytes, ct);
        await stream.WriteAsync(payload, ct);
    }

    private static byte[] EncodeVarint(uint value)
    {
        var bytes = new List<byte>();
        while (value >= 0x80)
        {
            bytes.Add((byte)(value | 0x80));
            value >>= 7;
        }
        bytes.Add((byte)value);
        return bytes.ToArray();
    }

    private static async Task<uint> ReadVarintAsync(Stream stream, CancellationToken ct)
    {
        uint result = 0;
        var shift = 0;
        while (true)
        {
            var b = await ReadByteAsync(stream, ct);
            result |= (uint)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return result;
            shift += 7;
        }
    }

    private static async Task<byte> ReadByteAsync(Stream stream, CancellationToken ct)
    {
        var buffer = new byte[1];
        await ReadExactAsync(stream, buffer, ct);
        return buffer[0];
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), ct);
            if (read == 0) throw new IOException("MCS connection closed mid-frame.");
            offset += read;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _lifetimeCts.Cancel();
        if (_readLoop is not null)
        {
            try { await _readLoop; } catch { /* already logged */ }
        }
        _ssl?.Dispose();
        _tcp?.Dispose();
        _lifetimeCts.Dispose();
    }
}
