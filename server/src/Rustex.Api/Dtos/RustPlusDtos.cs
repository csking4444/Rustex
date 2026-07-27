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

public record RustPlusTeamMemberStateResponse(
    ulong SteamId,
    string Name,
    bool IsOnline,
    bool IsAlive,
    float LastX,
    float LastY,
    string? LastGrid,
    DateTimeOffset LastSeenAt,
    DateTimeOffset UpdatedAt);

public record RustPlusVendingSearchResultResponse(
    int MarkerId,
    string? MachineName,
    string? Grid,
    int ItemId,
    string ItemName,
    int CostPerItem,
    int CurrencyId,
    string CurrencyName,
    bool CurrencyIsBlueprint,
    int AmountInStock,
    DateTimeOffset UpdatedAt);

public record CreateShopAlertRequest(
    int? ItemId,
    string? ItemNameContains,
    int? MaxCostPerItem,
    int MinAmountInStock = 1,
    bool NotifyOnNewListing = true,
    bool NotifyOnPriceDrop = true,
    bool NotifyOnRestock = true,
    int CooldownSeconds = 900);

public record UpdateShopAlertRequest(
    int? ItemId,
    string? ItemNameContains,
    int? MaxCostPerItem,
    int MinAmountInStock,
    bool NotifyOnNewListing,
    bool NotifyOnPriceDrop,
    bool NotifyOnRestock,
    bool IsEnabled,
    int CooldownSeconds);

public record RustPlusSmartDeviceResponse(
    Guid Id,
    long EntityId,
    string Type,
    string Name,
    bool? LastKnownValue,
    int? LastKnownCapacity,
    bool AlarmRaisesRaidEvent,
    DateTimeOffset? LastChangedAt,
    DateTimeOffset PairedAt);

public record CreateSmartDeviceRequest(long EntityId, string Type, string Name);

public record UpdateSmartDeviceRequest(string Name, bool AlarmRaisesRaidEvent);

public record SetSmartDeviceValueRequest(bool Value);

public record RustPlusChatMessageResponse(ulong SteamId, string Name, string Message, bool IsFromAssistant, DateTimeOffset SentAt);

public record SendRustPlusChatMessageRequest(string Message);

public record ShopAlertResponse(
    Guid Id,
    Guid ServerId,
    int? ItemId,
    string? ItemName,
    string? ItemNameContains,
    int? MaxCostPerItem,
    int MinAmountInStock,
    bool NotifyOnNewListing,
    bool NotifyOnPriceDrop,
    bool NotifyOnRestock,
    bool IsEnabled,
    int CooldownSeconds,
    DateTimeOffset? LastTriggeredAt,
    DateTimeOffset CreatedAt);
