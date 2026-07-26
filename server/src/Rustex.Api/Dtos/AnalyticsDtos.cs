namespace Rustex.Api.Dtos;

public record DailyRaidCount(DateOnly Date, int Count);

public record HourlyRaidCount(int HourUtc, int Count);

public record AnalyticsSummaryResponse(
    Guid ServerId,
    int Days,
    int TotalRaids,
    int Tier1Count,
    int Tier2Count,
    int Tier3Count,
    List<DailyRaidCount> RaidsByDay,
    List<HourlyRaidCount> RaidsByHour,
    double? AvgPingMs,
    double? AvgPlayerCount,
    int? PeakPlayerCount);
