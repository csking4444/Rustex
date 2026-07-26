namespace Rustex.Api.Dtos;

public record RaidAlarmSettingsResponse(
    Guid ServerId,
    bool IsEnabled,
    int Tier1Threshold,
    int Tier2Threshold,
    int Tier3Threshold,
    int TimeWindowSeconds,
    double ClusterRadius,
    int CooldownSeconds,
    DateTimeOffset UpdatedAt);

public record UpdateRaidAlarmSettingsRequest(
    bool IsEnabled,
    int Tier1Threshold,
    int Tier2Threshold,
    int Tier3Threshold,
    int TimeWindowSeconds,
    double ClusterRadius,
    int CooldownSeconds);
