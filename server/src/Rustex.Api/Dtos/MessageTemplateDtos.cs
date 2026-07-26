namespace Rustex.Api.Dtos;

public record MessageTemplateResponse(
    Guid Id,
    Guid TeamId,
    Guid? ServerId,
    string EventType,
    string TemplateText,
    bool IsEnabled,
    int CooldownSeconds,
    DateTimeOffset CreatedAt);

public record CreateMessageTemplateRequest(
    Guid? ServerId,
    string EventType,
    string TemplateText,
    bool IsEnabled,
    int CooldownSeconds);

public record UpdateMessageTemplateRequest(
    string TemplateText,
    bool IsEnabled,
    int CooldownSeconds);

public record PreviewTemplateRequest(string TemplateText, string? EventType);

public record PreviewTemplateResponse(string Rendered);

public record ChatTemplateMetadataResponse(IReadOnlyList<string> EventTypes, IReadOnlyList<string> Placeholders);
