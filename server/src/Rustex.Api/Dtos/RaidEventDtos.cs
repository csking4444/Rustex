using Rustex.Domain;

namespace Rustex.Api.Dtos;

public record RaidEventResponse(
    Guid Id,
    Guid ServerId,
    string ServerName,
    DateTimeOffset DetectedAt,
    string? Grid,
    RaidTier Tier,
    string? RaidType,
    int ExplosionCount,
    string? EstimatedSize,
    RaidStatus Status);
