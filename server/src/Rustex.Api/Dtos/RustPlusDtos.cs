namespace Rustex.Api.Dtos;

/// <summary>PlayerToken accepts either the signed or unsigned 32-bit rendering of a Rust+ token
/// — different community pairing tools print it differently — see RustPlusTokenFormat.</summary>
public record CreateRustPlusPairingRequest(ulong PlayerId, long PlayerToken, string ServerIp, int ServerPort);

public record RustPlusPairingResponse(
    Guid Id,
    Guid ServerId,
    ulong PlayerId,
    string ServerIp,
    int ServerPort,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastConnectedAt);

public record RustPlusTeamMemberResponse(ulong SteamId, string Name, float X, float Y, bool IsOnline, bool IsAlive);

public record RustPlusTeamInfoResponse(ulong LeaderSteamId, List<RustPlusTeamMemberResponse> Members);

public record RustPlusSellOrderResponse(int ItemId, int Quantity, int CurrencyId, int CostPerItem, int AmountInStock);

public record RustPlusVendingMachineResponse(int MarkerId, float X, float Y, List<RustPlusSellOrderResponse> SellOrders);
