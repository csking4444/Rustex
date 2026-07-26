using Rustex.Domain;

namespace Rustex.Api.Dtos;

public record MapResponse(Guid Id, Guid ServerId, string? ImageUrl, int? Width, int? Height, DateTimeOffset UpdatedAt);

public record UpdateMapRequest(string? ImageUrl, int? Width, int? Height);

public record MarkerResponse(
    Guid Id,
    Guid MapId,
    Guid CreatedBy,
    MarkerType Type,
    double X,
    double Y,
    string? Label,
    string? Color,
    bool IsShared,
    DateTimeOffset CreatedAt);

public record CreateMarkerRequest(MarkerType Type, double X, double Y, string? Label, string? Color, bool IsShared);

public record UpdateMarkerRequest(string? Label, string? Color, bool IsShared);
