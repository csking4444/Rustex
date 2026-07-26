using Rustex.Domain;

namespace Rustex.Api.Dtos;

public record ServerResponse(
    Guid Id,
    string Name,
    string IpAddress,
    int GamePort,
    int? QueryPort,
    string? MapName,
    long? Seed,
    int? WorldSize,
    string? Description,
    ServerStatus Status,
    List<string> Tags,
    bool IsFavorite,
    string? WipeSchedule,
    string? RestartSchedule,
    bool AutoReconnect,
    DateTimeOffset CreatedAt,
    int? PingMs,
    int? PlayerCount,
    int? MaxPlayers,
    int? QueueSize,
    DateTimeOffset? LastPolledAt);

public record CreateServerRequest(
    string Name,
    string IpAddress,
    int GamePort,
    int? QueryPort,
    string? MapName,
    long? Seed,
    int? WorldSize,
    string? Description,
    List<string>? Tags,
    string? WipeSchedule,
    string? RestartSchedule);

public record UpdateServerRequest(
    string Name,
    string IpAddress,
    int GamePort,
    int? QueryPort,
    string? MapName,
    long? Seed,
    int? WorldSize,
    string? Description,
    List<string>? Tags,
    bool IsFavorite,
    string? WipeSchedule,
    string? RestartSchedule,
    bool AutoReconnect);
