using Rustex.Domain;

namespace Rustex.Api.Dtos;

public record NotificationResponse(
    Guid Id,
    string Type,
    string Title,
    string? Body,
    NotificationSeverity Severity,
    bool IsRead,
    string? RelatedEntityType,
    Guid? RelatedEntityId,
    DateTimeOffset CreatedAt);
