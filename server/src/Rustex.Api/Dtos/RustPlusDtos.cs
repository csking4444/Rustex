namespace Rustex.Api.Dtos;

public record CreateRustPlusPairingRequest(ulong PlayerId, string PlayerToken, string ServerIp, int ServerPort);

/// <summary>SteamAuthTicketHex must come from the user's own Steam client (a session auth
/// ticket for AppID 252490 / Rust) — see docs/RUSTPLUS.md for why this can't be generated
/// server-side.</summary>
public record AutoPairRequest(string SteamAuthTicketHex);

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
